using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Domain.Enums.Patients;
using SCI_PH.IntegrationTests.Clinical.Support;
using SCI_PH.IntegrationTests.PostgreSql;
using SCI_PH.IntegrationTests.Security.Support;

namespace SCI_PH.IntegrationTests.Clinical.Http
{
    // Registration with a document AND a contact email: before, only one identifier could be given, so a patient registered
    // with a cédula never had an email and could never be invited to the patient app (SubjectHasNoEmail). The email is an
    // extra, non-unique Email identifier; the invitation goes to it.
    [Collection(PostgresCollection.Name)]
    public sealed class PatientRegistrationEmailApiTests
    {
        private const string Patients = "/api/web/patients";
        private const string InvitePatient = "/api/web/admin/invitations/patients";
        private readonly PostgresFixture _postgres;

        public PatientRegistrationEmailApiTests(PostgresFixture postgres) => _postgres = postgres;

        private static object Body(string nationalId, string? email = null) => new
        {
            givenNames = "Laura",
            familyNames = "Méndez",
            dateOfBirth = "1991-04-02",
            sex = "Female",
            identifier = new { type = "NationalId", value = nationalId },
            email
        };

        [SkippableFact]
        public async Task APatientRegisteredWithDocumentAndEmailCanBeInvited()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await ClinicalApiHost.StartAsync(_postgres);
            var reception = await host.SignInAsync(SecurityRoles.Receptionist, [await host.CreateClinicAsync()]);
            var nationalId = ClinicalApiHost.RandomNationalId();

            var registered = await reception.Client.PostAsJsonAsync(Patients, Body(nationalId, "  Laura.Mendez@Correo.test "));

            Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
            var patient = await registered.ReadAsAsync<PatientSummary>();
            // The document stays the primary identifier; the email is stored normalized next to it.
            Assert.Equal(PatientIdentifierType.NationalId, patient.PrimaryIdentifier!.Type);
            var identifiers = await host.DbAsync(db => db.PatientIdentifiers.AsNoTracking().Where(i => i.PatientId == patient.Id).ToListAsync());
            Assert.Equal(2, identifiers.Count);
            var email = Assert.Single(identifiers, i => i.Type == PatientIdentifierType.Email);
            Assert.Equal(("laura.mendez@correo.test", false), (email.NormalizedValue, email.IsPrimary));

            var invited = await reception.Client.PostAsJsonAsync(InvitePatient, new { patientId = patient.Id });
            Assert.Equal(HttpStatusCode.Created, invited.StatusCode);
            Assert.Equal("laura.mendez@correo.test", Assert.Single(host.Factory.Email.Sent).To);
        }

        [SkippableFact]
        public async Task WithoutAnEmailTheInvitationStillExplainsWhy()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await ClinicalApiHost.StartAsync(_postgres);
            var reception = await host.SignInAsync(SecurityRoles.Receptionist, [await host.CreateClinicAsync()]);

            var patient = await (await reception.Client.PostAsJsonAsync(Patients, Body(ClinicalApiHost.RandomNationalId())))
                .ReadAsAsync<PatientSummary>();

            Assert.Equal(HttpStatusCode.BadRequest, (await reception.Client.PostAsJsonAsync(InvitePatient, new { patientId = patient.Id })).StatusCode);
            Assert.Empty(host.Factory.Email.Sent);
        }

        [SkippableFact]
        public async Task AnInvalidEmailRegistersNothing()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await ClinicalApiHost.StartAsync(_postgres);
            var reception = await host.SignInAsync(SecurityRoles.Receptionist, [await host.CreateClinicAsync()]);
            var before = await host.DbAsync(db => db.Patients.CountAsync());

            var response = await reception.Client.PostAsJsonAsync(Patients, Body(ClinicalApiHost.RandomNationalId(), "sin-arroba"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(before, await host.DbAsync(db => db.Patients.CountAsync()));
        }

        [SkippableFact]
        public async Task TheSameEmailAsTheFirstIdentifierIsNotDuplicated()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await ClinicalApiHost.StartAsync(_postgres);
            var reception = await host.SignInAsync(SecurityRoles.Receptionist, [await host.CreateClinicAsync()]);

            var patient = await (await reception.Client.PostAsJsonAsync(Patients, new
            {
                givenNames = "Pedro",
                familyNames = "Rosa",
                sex = "Male",
                identifier = new { type = "Email", value = "pedro@correo.test" },
                email = "Pedro@Correo.test"
            })).ReadAsAsync<PatientSummary>();

            Assert.Single(await host.DbAsync(db => db.PatientIdentifiers.AsNoTracking().Where(i => i.PatientId == patient.Id).ToListAsync()));
        }
    }
}
