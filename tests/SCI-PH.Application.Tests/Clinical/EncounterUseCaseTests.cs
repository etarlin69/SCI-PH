using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Enums.Patients;

namespace SCI_PH.Application.Tests.Clinical
{
    public class EncounterUseCaseTests
    {
        private static readonly CancellationToken None = CancellationToken.None;

        [Fact]
        public async Task IdentifiedEncounterStartsCreatedAtTheActorsClinicAndIsAudited()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Receptionist);
            var patient = harness.SeedPatient();

            var encounter = await harness.Encounters.CreateAsync(
                new CreateEncounterRequest(ClinicalEncounterType.Consultation, patient.Id, "Control"), None);

            Assert.Equal(ClinicalEncounterStatus.Created, encounter.Status);
            Assert.Equal(harness.ClinicId, encounter.ClinicId);
            Assert.Equal(patient.Id, encounter.Patient!.Id);
            Assert.Null(encounter.TemporaryPatientCode);
            Assert.Equal(ClinicalHarness.Start, encounter.StartedAtUtc);
            Assert.Equal(AuditActionType.Create, Assert.Single(harness.Store.Audit).Action);
        }

        [Fact]
        public async Task MissingPatientIsNotFound()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Receptionist);

            await Assert.ThrowsAsync<ResourceNotFoundException>(() => harness.Encounters.CreateAsync(
                new CreateEncounterRequest(ClinicalEncounterType.Emergency, Guid.NewGuid()), None));
            Assert.Empty(harness.Store.Encounters);
        }

        [Fact]
        public async Task EmergencyAllowsAnUnidentifiedPatient()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Receptionist);

            var encounter = await harness.Encounters.CreateAsync(new CreateEncounterRequest(ClinicalEncounterType.Emergency), None);

            Assert.Null(encounter.PatientId);
            Assert.StartsWith("NN-", encounter.TemporaryPatientCode);
        }

        [Theory]
        [InlineData(ClinicalEncounterType.Consultation)]
        [InlineData(ClinicalEncounterType.Hospitalization)]
        public async Task OtherTypesRejectAnUnidentifiedPatient(ClinicalEncounterType type)
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Receptionist);

            var error = await Assert.ThrowsAsync<RequestValidationException>(() => harness.Encounters.CreateAsync(new CreateEncounterRequest(type), None));
            Assert.Equal("patientId", error.Field);
        }

        [Fact]
        public async Task CreatingAtAForeignClinicIsDenied()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Receptionist);

            await Assert.ThrowsAsync<AccessDeniedException>(() => harness.Encounters.CreateAsync(
                new CreateEncounterRequest(ClinicalEncounterType.Emergency, ClinicId: Guid.NewGuid()), None));
        }

        [Fact]
        public async Task ReadingAnEncounterOfAnotherClinicIsDenied()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Nurse);
            var foreign = harness.SeedEncounter(clinicId: Guid.NewGuid());

            await Assert.ThrowsAsync<AccessDeniedException>(() => harness.Encounters.GetAsync(foreign.Id, None));
            await Assert.ThrowsAsync<AccessDeniedException>(() => harness.Records.ListTriagesAsync(foreign.Id, None));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => harness.Encounters.GetAsync(Guid.NewGuid(), None));
        }

        [Fact]
        public async Task ListingWithSeveralClinicsNeedsAClinicAndOnlyShowsThatClinic()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Nurse, clinics: 2);
            var clinics = (await harness.Actor.GetAsync(None)).ActiveClinicIds.ToArray();
            var first = harness.SeedEncounter(clinics[0]);
            harness.SeedEncounter(clinics[1]);

            await Assert.ThrowsAsync<RequestValidationException>(() => harness.Encounters.ListOpenAsync(null, None));
            Assert.Equal(first.Id, Assert.Single(await harness.Encounters.ListOpenAsync(clinics[0], None)).Id);
        }

        [Fact]
        public async Task IdentifyingKeepsTheTemporaryCodeAndAuditsTheChange()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Receptionist);
            var patient = harness.SeedPatient();
            var encounter = harness.SeedEncounter();
            var code = encounter.TemporaryPatientCode!.Value;
            harness.Time.Now = ClinicalHarness.Start.AddMinutes(30);

            await harness.Encounters.IdentifyPatientAsync(encounter.Id, new IdentifyEncounterPatientRequest(patient.Id), None);

            Assert.Equal(patient.Id, encounter.PatientId);
            Assert.Equal(code, encounter.TemporaryPatientCode!.Value);
            Assert.Equal(harness.Time.Now, encounter.IdentifiedAtUtc);
            var audit = Assert.Single(harness.Store.Audit);
            Assert.Equal(AuditActionType.Identify, audit.Action);
            Assert.Equal(patient.Id, audit.PatientId);
            Assert.Contains(code, audit.ChangesJson);
            Assert.Equal(1, harness.Store.SaveCount);
        }

        [Fact]
        public async Task IdentifyingTwiceIsAConflictAndAMissingPatientIsNotFound()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Receptionist);
            var patient = harness.SeedPatient();
            var encounter = harness.SeedEncounter();

            await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
                harness.Encounters.IdentifyPatientAsync(encounter.Id, new IdentifyEncounterPatientRequest(Guid.NewGuid()), None));
            await harness.Encounters.IdentifyPatientAsync(encounter.Id, new IdentifyEncounterPatientRequest(patient.Id), None);
            await Assert.ThrowsAsync<ResourceConflictException>(() =>
                harness.Encounters.IdentifyPatientAsync(encounter.Id, new IdentifyEncounterPatientRequest(harness.SeedPatient().Id), None));
        }

        [Fact]
        public async Task AdmissionProjectionHasNoClinicalContentAndFiltersUnidentified()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Receptionist);
            var patient = harness.SeedPatient();
            harness.SeedEncounter(patientId: patient.Id);
            var nn = harness.SeedEncounter();

            var all = await harness.Admissions.ListAsync(null, onlyUnidentified: false, None);
            var unidentified = await harness.Admissions.ListAsync(null, onlyUnidentified: true, None);

            Assert.Equal(2, all.Count);
            Assert.Equal(nn.Id, Assert.Single(unidentified).EncounterId);
            Assert.Contains(all, a => a.PatientDisplayName == "Ana Pérez");
            Assert.DoesNotContain(typeof(AdmissionEncounterSummary).GetProperties(), p =>
                p.Name is "Reason" or "LatestTriagePriority" or "Patient" || p.Name.Contains("Vital") || p.Name.Contains("Triage"));
        }

        [Fact]
        public async Task SearchMatchesTheNormalizedIdentifierAndMasksIt()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Receptionist);
            harness.SeedPatient();

            var result = Assert.Single(await harness.Patients.SearchAsync(
                new PatientSearchRequest(PatientIdentifierType.NationalId, " 00112345678 "), None));

            Assert.Equal("***5678", result.PrimaryIdentifier!.Value);
            Assert.DoesNotContain("1234567", result.PrimaryIdentifier.Value);
        }

        [Fact]
        public async Task NameSearchNeedsThreeCharacters()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Receptionist);
            harness.SeedPatient();

            await Assert.ThrowsAsync<RequestValidationException>(() => harness.Patients.SearchAsync(new PatientSearchRequest(null, "An"), None));
            Assert.Single(await harness.Patients.SearchAsync(new PatientSearchRequest(null, "ana p"), None));
        }

        [Fact]
        public async Task RegistrationNormalizesTheIdentifierAndRejectsADuplicate()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Receptionist);
            var request = new RegisterPatientRequest("Luis", "Gómez", new DateOnly(1990, 5, 2), BiologicalSex.Male,
                new PatientIdentifierInput(PatientIdentifierType.NationalId, "402-0000001-2"));

            var patient = await harness.Patients.RegisterAsync(request, None);

            Assert.Equal("40200000012", Assert.Single(harness.Store.Patients.Single().Identifiers).NormalizedValue);
            Assert.Equal("***0012", patient.PrimaryIdentifier!.Value);
            Assert.Equal(AuditActionType.Create, Assert.Single(harness.Store.Audit).Action);
            await Assert.ThrowsAsync<ResourceConflictException>(() => harness.Patients.RegisterAsync(
                request with { Identifier = new PatientIdentifierInput(PatientIdentifierType.NationalId, "40200000012") }, None));
        }

        [Fact]
        public async Task RegistrationRejectsAFutureBirthDateAndInvalidNames()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Receptionist);

            await Assert.ThrowsAsync<RequestValidationException>(() => harness.Patients.RegisterAsync(
                new RegisterPatientRequest("Luis", "Gómez", DateOnly.FromDateTime(ClinicalHarness.Start.UtcDateTime).AddDays(1), BiologicalSex.Male), None));
            var blank = await Assert.ThrowsAsync<RequestValidationException>(() => harness.Patients.RegisterAsync(
                new RegisterPatientRequest(" ", "Gómez", null, BiologicalSex.Male), None));
            Assert.Equal("givenNames", blank.Field);
            Assert.Empty(harness.Store.Patients);
        }
    }
}
