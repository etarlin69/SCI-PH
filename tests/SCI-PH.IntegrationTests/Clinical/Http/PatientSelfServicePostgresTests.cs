using System.Net;
using System.Net.Http.Json;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.IntegrationTests.Clinical.Support;
using SCI_PH.IntegrationTests.PostgreSql;
using SCI_PH.IntegrationTests.Security.Support;

namespace SCI_PH.IntegrationTests.Clinical.Http
{
    // The patient self-service queries over the migrated PostgreSQL schema (real SQL, foreign keys and ordering): each
    // of two linked patients reads exactly their own record through the Mobile BFF, and the other one's encounter is 404.
    [Collection(PostgresCollection.Name)]
    public sealed class PatientSelfServicePostgresTests
    {
        private const string Me = "/api/mobile/patient/me";
        private readonly PostgresFixture _postgres;

        public PatientSelfServicePostgresTests(PostgresFixture postgres) => _postgres = postgres;

        [SkippableFact]
        public async Task TwoPatientsReadOnlyTheirOwnRecordOverPostgres()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var (a, clientA) = await host.SignInPatientAsync("Alba");
            var (b, clientB) = await host.SignInPatientAsync("Bruno");

            foreach (var (own, other, client) in new[] { (a, b, clientA), (b, a, clientB) })
            {
                var profile = await client.GetFromJsonAsync<PatientSelfProfile>(Me);
                var summary = await client.GetFromJsonAsync<PatientSelfSummary>($"{Me}/summary");
                var encounters = (await client.GetFromJsonAsync<List<PatientSelfEncounter>>($"{Me}/encounters"))!;
                var allergies = await client.GetFromJsonAsync<List<PatientSelfAllergy>>($"{Me}/allergies");
                var diagnoses = await client.GetFromJsonAsync<List<PatientSelfDiagnosis>>($"{Me}/diagnoses");
                var prescriptions = await client.GetFromJsonAsync<List<PatientSelfPrescription>>($"{Me}/prescriptions");
                var results = await client.GetFromJsonAsync<List<PatientSelfResult>>($"{Me}/results");
                var detail = await client.GetFromJsonAsync<PatientSelfEncounterDetail>($"{Me}/encounters/{own.EncounterId}");

                Assert.Equal(own.GivenNames, profile!.GivenNames);
                Assert.Equal((2, 1, own.EncounterId, own.FinalResultId), (summary!.EncounterCount, summary.ResultCount, summary.LatestEncounter!.Id, summary.LatestResult!.Id));
                Assert.Equal([own.EncounterId, own.OlderEncounterId], encounters.Select(e => e.Id));
                Assert.Equal($"Hospital {own.GivenNames}", encounters[0].ClinicName);
                Assert.Equal(own.AllergyId, Assert.Single(allergies!).Id);
                Assert.Equal(own.DiagnosisId, Assert.Single(diagnoses!).Id);
                Assert.Equal(own.IssuedPrescriptionId, Assert.Single(prescriptions!).Id);
                Assert.Single(prescriptions![0].Items);
                Assert.Equal(own.FinalResultId, Assert.Single(results!).Id);
                Assert.Equal((own.DiagnosisId, own.FinalResultId, own.IssuedPrescriptionId),
                    (Assert.Single(detail!.Diagnoses).Id, Assert.Single(detail.Results).Id, Assert.Single(detail.Prescriptions).Id));
                Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{Me}/encounters/{other.EncounterId}")).StatusCode);
            }

            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync()]);
            Assert.Equal(HttpStatusCode.Forbidden, (await paramedic.Client.GetAsync(Me)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await clientA.GetAsync($"/api/mobile/emergencies/{a.EncounterId}")).StatusCode);
        }
    }
}
