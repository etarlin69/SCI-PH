using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Exceptions;

namespace SCI_PH.Domain.Tests.Encounters
{
    public class EncounterRecordsTests
    {
        private static readonly Guid EncounterId = Guid.NewGuid();
        private static readonly Guid StaffId = Guid.NewGuid();
        private static readonly DateTimeOffset At = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

        [Fact]
        public void VitalSignsKeepEveryMeasurementWithItsCanonicalUnit()
        {
            var readings = new[]
            {
                VitalSign.Record(EncounterId, VitalSignType.OxygenSaturation, 88m, At, StaffId),
                VitalSign.Record(EncounterId, VitalSignType.OxygenSaturation, 94m, At.AddMinutes(15), StaffId),
                VitalSign.Record(EncounterId, VitalSignType.Temperature, 38.4m, At.AddMinutes(15))
            };

            Assert.Equal(3, readings.Select(r => r.Id).Distinct().Count());
            Assert.All(readings, r => Assert.Equal(EncounterId, r.ClinicalEncounterId));
            Assert.Equal("%", readings[0].Unit);
            Assert.Equal("Cel", readings[2].Unit);
            Assert.Null(readings[2].MeasuredByStaffId);
        }

        [Theory]
        [InlineData(VitalSignType.OxygenSaturation, 101)]
        [InlineData(VitalSignType.Temperature, 50)]
        [InlineData(VitalSignType.HeartRate, -1)]
        [InlineData(VitalSignType.GlasgowComaScale, 2)]
        public void ImplausibleVitalSignsAreRejected(VitalSignType type, int value)
        {
            Assert.Throws<DomainException>(() => VitalSign.Record(EncounterId, type, value, At));
        }

        [Fact]
        public void EveryVitalSignTypeHasLimits()
        {
            Assert.All(Enum.GetValues<VitalSignType>(), type => Assert.True(VitalSignLimits.ByType.ContainsKey(type)));
        }

        [Fact]
        public void EncounterCanBeTriagedMoreThanOnce()
        {
            var first = Triage.Record(EncounterId, TriagePriority.Urgent, StaffId, At, "Dolor torácico");
            var second = Triage.Record(EncounterId, TriagePriority.Immediate, StaffId, At.AddMinutes(20), observations: "Empeora");

            Assert.NotEqual(first.Id, second.Id);
            Assert.Equal(TriagePriority.Immediate, second.Priority);
        }

        [Fact]
        public void NotesRequireContentAndKeepClinicalTime()
        {
            var note = ClinicalNote.Write(EncounterId, StaffId, ClinicalNoteType.Prehospital, "Paciente consciente", At);

            Assert.Equal(At, note.OccurredAtUtc);
            Assert.Throws<ArgumentException>(() => ClinicalNote.Write(EncounterId, StaffId, ClinicalNoteType.Evolution, " ", At));
        }

        [Fact]
        public void PresumptiveDiagnosisCanBeConfirmed()
        {
            var diagnosis = Diagnosis.Record(EncounterId, StaffId, "Neumonía", DiagnosisType.Presumptive, At, isPrimary: true);
            Assert.Null(diagnosis.ConfirmedAtUtc);

            diagnosis.ConfirmAsDefinitive(At.AddHours(2));

            Assert.Equal(DiagnosisType.Definitive, diagnosis.Type);
            Assert.Equal(At.AddHours(2), diagnosis.ConfirmedAtUtc);
            Assert.Throws<DomainException>(() => diagnosis.ConfirmAsDefinitive(At.AddHours(3)));
        }

        [Fact]
        public void ProcedureFollowsItsLifecycle()
        {
            var procedure = ClinicalProcedure.Plan(EncounterId, "Intubación orotraqueal");
            Assert.Throws<DomainException>(() => procedure.Complete(At));

            procedure.Start(StaffId, At);
            procedure.Complete(At.AddMinutes(10), "Sin complicaciones");

            Assert.Equal(ClinicalProcedureStatus.Completed, procedure.Status);
            Assert.Throws<DomainException>(() => procedure.Cancel());
        }

        [Fact]
        public void ProcedureDocumentedAfterTheFactIsCompleted()
        {
            var procedure = ClinicalProcedure.RecordCompleted(EncounterId, "Canalización venosa", StaffId, At, At.AddMinutes(3));

            Assert.Equal(ClinicalProcedureStatus.Completed, procedure.Status);
            Assert.Equal(StaffId, procedure.PerformedByStaffId);
        }

        [Fact]
        public void ResultNeedsAValueWhenFinalAndCanBeCorrected()
        {
            var result = ClinicalResult.Request(EncounterId, ClinicalResultType.Laboratory, "Glucosa");
            Assert.Throws<DomainException>(() => result.RecordFinal(At));

            result.RecordFinal(At, numericValue: 180m, unit: "mg/dL");
            result.Correct(At.AddHours(1), numericValue: 160m, unit: "mg/dL");

            Assert.Equal(ClinicalResultStatus.Corrected, result.Status);
            Assert.Equal(160m, result.NumericValue);
            Assert.Throws<DomainException>(() => result.Cancel());
        }

        [Fact]
        public void StaffAssignmentCanEndAndBeRepeated()
        {
            var first = EncounterStaffAssignment.Assign(EncounterId, StaffId, EncounterStaffFunction.Nursing, At);
            first.Unassign(At.AddHours(8));
            var again = EncounterStaffAssignment.Assign(EncounterId, StaffId, EncounterStaffFunction.Nursing, At.AddHours(20));

            Assert.False(first.IsActive);
            Assert.True(again.IsActive);
            Assert.Throws<DomainException>(() => first.Unassign(At.AddHours(9)));
        }
    }
}
