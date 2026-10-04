using SCI_PH.Domain.Entities.Auditing;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.ValueObjects;

namespace SCI_PH.Domain.Tests.Auditing
{
    public class ClinicalAuditEntryTests
    {
        private static readonly DateTimeOffset At = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

        [Fact]
        public void UserActionIsRecordedWithAccountAndStaff()
        {
            var user = Guid.NewGuid();
            var staff = Guid.NewGuid();
            var patient = Guid.NewGuid();

            var entry = ClinicalAuditEntry.Success(
                AuditActor.User(user, staff), AuditActionType.Identify, "ClinicalEncounter", "42", At, new AuditContext(PatientId: patient));

            Assert.Equal(AuditActorType.User, entry.ActorType);
            Assert.Equal(user, entry.ActorIdentityUserId);
            Assert.Equal(staff, entry.ActorStaffId);
            Assert.Null(entry.ActorReference);
            Assert.Equal(AuditOutcome.Success, entry.Outcome);
            Assert.Null(entry.FailureReason);
            Assert.Equal(patient, entry.PatientId);
        }

        [Fact]
        public void SystemAndDeviceActorsHaveReferenceButNoAccount()
        {
            var system = ClinicalAuditEntry.Success(AuditActor.System("document-cleanup-job"), AuditActionType.Delete, "ClinicalDocument", "7", At);
            var device = ClinicalAuditEntry.Failure(
                AuditActor.Device("ESP32-R307-01"), AuditActionType.Read, "Patient", "unknown", At, "Fingerprint did not match");

            Assert.Equal(AuditActorType.System, system.ActorType);
            Assert.Null(system.ActorIdentityUserId);
            Assert.Equal(AuditActorType.Device, device.ActorType);
            Assert.Equal("ESP32-R307-01", device.ActorReference);
            Assert.Equal(AuditOutcome.Failure, device.Outcome);
            Assert.Equal("Fingerprint did not match", device.FailureReason);
        }

        [Fact]
        public void ActorsAndFailuresMustBeComplete()
        {
            Assert.Throws<ArgumentException>(() => AuditActor.User(Guid.Empty));
            Assert.Throws<ArgumentException>(() => AuditActor.Device(" "));
            Assert.Throws<ArgumentException>(() =>
                ClinicalAuditEntry.Failure(AuditActor.System("job"), AuditActionType.Update, "Patient", "1", At, ""));
        }

        [Fact]
        public void AuditEntryHasNoPublicSetters()
        {
            Assert.All(typeof(ClinicalAuditEntry).GetProperties(), property => Assert.False(property.SetMethod?.IsPublic ?? false));
        }
    }
}
