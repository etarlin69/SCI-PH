using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.Features.Clinical;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Enums.Organizations;

namespace SCI_PH.Application.Tests.Clinical
{
    // The hospital reads the procedures the paramedic recorded (EncounterRecordService.ListProceduresAsync) in the SAME
    // encounter once it receives it, with the same rule as notes and diagnoses: never before, and never a clinic that does
    // not hold the encounter.
    public class HospitalProcedureReadTests
    {
        private static readonly CancellationToken None = CancellationToken.None;

        private static async Task<(ClinicalHarness Paramedic, ClinicalEncounter Emergency, Guid TransferId, Guid Hospital)> InTransportAsync()
        {
            var paramedic = ClinicalHarness.ForRole(SecurityRoles.Paramedic);
            var opened = await paramedic.Emergencies.OpenAsync(
                new OpenPrehospitalEmergencyRequest(Guid.NewGuid(), ClinicalHarness.Start.AddMinutes(-10), "Disnea"), None);
            var emergency = paramedic.Store.Encounters.Single(e => e.Id == opened.Emergency.Id);
            await paramedic.Emergencies.RecordProcedureAsync(emergency.Id,
                new RecordEmergencyProcedureRequest(Guid.NewGuid(), "OXYGEN-THERAPY", ClinicalHarness.Start.AddMinutes(-5), "SpO2 97 %"), None);
            var hospital = Guid.NewGuid();
            paramedic.Store.Clinics[hospital] = ClinicStatus.Active;
            var requested = await paramedic.Emergencies.RequestTransferAsync(
                emergency.Id, new RequestEmergencyTransferRequest(Guid.NewGuid(), hospital, null, null), None);
            var started = await paramedic.Emergencies.StartTransferAsync(emergency.Id, requested.Transfer.Id, None);
            return (paramedic, emergency, started.Id, hospital);
        }

        private static ClinicalHarness DoctorAt(ClinicalHarness paramedic, Guid clinic)
        {
            var doctor = new ClinicalHarness(FakeCurrentUser.InRole(SecurityRoles.Doctor), paramedic.Store);
            doctor.Staff.Add(doctor.User.UserId!.Value, true, clinic);
            return doctor;
        }

        [Fact]
        public async Task TheReceivingHospitalReadsTheParamedicsProcedureInTheSameEncounter()
        {
            var (paramedic, emergency, transferId, hospital) = await InTransportAsync();
            var code = emergency.TemporaryPatientCode!.Value;
            var doctor = DoctorAt(paramedic, hospital);
            await doctor.Transfers.ReceiveAsync(transferId, None);

            var procedure = Assert.Single(await doctor.Records.ListProceduresAsync(emergency.Id, None));

            Assert.Equal((emergency.Id, "Oxigenoterapia", ClinicalProcedureStatus.Completed, "SpO2 97 %"),
                (procedure.EncounterId, procedure.Name, procedure.Status, procedure.Outcome));
            Assert.Equal((PrehospitalProcedureCatalog.CodeSystem, "OXYGEN-THERAPY"), (procedure.CodeSystem, procedure.Code));
            Assert.True(procedure.Prehospital);
            Assert.Equal(ClinicalHarness.Start.AddMinutes(-5), procedure.PerformedAtUtc);
            // Same encounter, NN trace kept, no copy of the procedure.
            Assert.Single(paramedic.Store.Encounters);
            Assert.Equal(code, emergency.TemporaryPatientCode!.Value);
            Assert.Single(paramedic.Store.Procedures);
        }

        [Fact]
        public async Task BeforeTheReceptionTheHospitalCannotReadThem()
        {
            var (paramedic, emergency, _, hospital) = await InTransportAsync();
            var doctor = DoctorAt(paramedic, hospital);

            await Assert.ThrowsAsync<AccessDeniedException>(() => doctor.Records.ListProceduresAsync(emergency.Id, None));
        }

        [Fact]
        public async Task AnotherClinicNeverReadsThem()
        {
            var (paramedic, emergency, transferId, hospital) = await InTransportAsync();
            await DoctorAt(paramedic, hospital).Transfers.ReceiveAsync(transferId, None);
            var other = DoctorAt(paramedic, Guid.NewGuid());

            await Assert.ThrowsAsync<AccessDeniedException>(() => other.Records.ListProceduresAsync(emergency.Id, None));
        }

        [Fact]
        public void AProcedureOutsideThePrehospitalCatalogIsNotMarkedPrehospital()
        {
            var summary = new ClinicalProcedureSummary(
                Guid.NewGuid(), Guid.NewGuid(), "Sutura", null, null, ClinicalProcedureStatus.Completed, ClinicalHarness.Start, ClinicalHarness.Start, null);

            Assert.False(summary.Prehospital);
            Assert.Null(summary.PerformedByDisplayName);
        }
    }
}
