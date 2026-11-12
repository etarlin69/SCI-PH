using SCI_PH.Application.Abstractions.Biometrics;
using SCI_PH.Application.Common.Biometrics;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Biometrics;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.Application.Features.Clinical;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Patients;

namespace SCI_PH.Application.Tests.Clinical
{
    // Fingerprint revocation (FingerprintRevocationService for hospital staff, FingerprintSlotDeletionReaderService for the
    // reader): the reference becomes Revoked with its audit entry and ONE deletion job in the same save, it identifies nobody
    // from then on, the reader deletes its slot attempt by attempt (attemptId), and a slot waiting for that deletion cannot
    // hold a new enrollment.
    public class FingerprintRevocationTests
    {
        private static readonly CancellationToken None = CancellationToken.None;
        private const int Slot = 4;

        // T0: the server's now, after the reference was registered (PatientBiometricReference.Register takes the wall clock).
        private sealed record Scenario(ClinicalHarness Reception, Patient Patient, DeviceSummary Reader, PatientBiometricReference Reference, DateTimeOffset T0);

        private static Scenario Start()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Receptionist);
            var patient = Patient.Register("Ana", "Pérez", new DateOnly(1985, 3, 14), BiologicalSex.Female);
            harness.Store.Patients.Add(patient);
            var reader = harness.Readers.Add("ESP32-HOSP-01", DeviceCredentialStatus.Active, DeviceScopes.FingerprintEnroll, DeviceScopes.FingerprintIdentify);
            var reference = Register(harness, patient, FingerprintReferences.SubjectReference(reader.Id, Slot));
            harness.Time.Now = reference.CreatedAtUtc.AddMinutes(1);
            return new Scenario(harness, patient, reader, reference, harness.Time.Now);
        }

        private static PatientBiometricReference Register(ClinicalHarness harness, Patient patient, string subjectReference, string provider = "R307")
        {
            var reference = PatientBiometricReference.Register(patient.Id, BiometricModality.Fingerprint, provider, subjectReference);
            harness.Store.BiometricReferences.Add(reference);
            return reference;
        }

        private static Task<PatientFingerprintSummary> RevokeAsync(Scenario s, FingerprintRevocationReason? reason = null, Guid? referenceId = null, Guid? patientId = null) =>
            s.Reception.FingerprintRevocations.RevokeAsync(
                patientId ?? s.Patient.Id, referenceId ?? s.Reference.Id, new RevokeFingerprintRequest(reason), None);

        private static Task ReportAsync(Scenario s, Guid deletionId, Guid attemptId, FingerprintSlotDeletionResultOutcome outcome, DeviceSummary? reader = null) =>
            s.Reception.DeletionReaderSide.SubmitResultAsync(
                (reader ?? s.Reader).Id, (reader ?? s.Reader).DeviceCode, deletionId, new SubmitFingerprintSlotDeletionResultRequest(attemptId, outcome), None);

        private static FingerprintSlotDeletion Job(Scenario s) => Assert.Single(s.Reception.Store.Deletions);

        private static async Task<FingerprintCleanupStatus?> CleanupAsync(Scenario s) =>
            (await s.Reception.FingerprintEnrollments.ListPatientFingerprintsAsync(s.Patient.Id, None)).Single(f => f.Id == s.Reference.Id).Cleanup;

        // Security first: Revoked, its Update audit entry (with the optional reason), ONE Pending job of its reader and its
        // Create audit entry, all in ONE save. No slot or subject reference in any audit entry.
        [Fact]
        public async Task RevokingQueuesOneDeletionJobInTheSameSave()
        {
            var s = Start();
            var saves = s.Reception.Store.SaveCount;

            var revoked = await RevokeAsync(s, FingerprintRevocationReason.PatientRequest);

            Assert.Equal(saves + 1, s.Reception.Store.SaveCount);
            Assert.Equal((BiometricReferenceStatus.Revoked, (DateTimeOffset?)s.T0), (s.Reference.Status, s.Reference.RevokedAtUtc));
            Assert.Equal((s.Reference.Id, BiometricReferenceStatus.Revoked, (DateTimeOffset?)s.T0, (FingerprintCleanupStatus?)FingerprintCleanupStatus.Pending),
                (revoked.Id, revoked.Status, revoked.RevokedAtUtc, revoked.Cleanup));
            Assert.Equal(new FingerprintReferenceReader("ESP32-HOSP-01", "Lector ESP32-HOSP-01", true), revoked.Reader);
            var job = Job(s);
            Assert.Equal((s.Reference.Id, s.Reader.Id, FingerprintSlotDeletionStatus.Pending, 0, s.T0),
                (job.BiometricReferenceId, job.DeviceCredentialId, job.Status, job.Attempts, job.CreatedAtUtc));
            var revocation = Assert.Single(s.Reception.Store.Audit, a => a.EntityName == nameof(PatientBiometricReference));
            Assert.Equal((AuditActionType.Update, (Guid?)s.Patient.Id, s.Reference.Id.ToString()), (revocation.Action, revocation.PatientId, revocation.EntityId));
            Assert.Contains("PatientRequest", revocation.ChangesJson);
            var requested = Assert.Single(s.Reception.Store.Audit, a => a.EntityName == FingerprintRevocationService.DeletionEntityName);
            Assert.Equal((AuditActionType.Create, job.Id.ToString()), (requested.Action, requested.EntityId));
            Assert.All(s.Reception.Store.Audit, a =>
            {
                Assert.DoesNotContain("r307", a.ChangesJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("slot", a.ChangesJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            });
            Assert.Equal(FingerprintCleanupStatus.Pending, await CleanupAsync(s));
        }

        // A replay answers the current state and writes nothing: no audit entry, no second job, the same revocation time.
        [Fact]
        public async Task RevokingAgainChangesNothing()
        {
            var s = Start();
            var first = await RevokeAsync(s);
            var (audit, saves) = (s.Reception.Store.Audit.Count, s.Reception.Store.SaveCount);
            s.Reception.Time.Now = s.T0.AddMinutes(10);

            var again = await RevokeAsync(s, FingerprintRevocationReason.Other);

            Assert.Equal(first, again);
            Assert.Equal((audit, saves), (s.Reception.Store.Audit.Count, s.Reception.Store.SaveCount));
            Assert.Equal(s.T0, s.Reference.RevokedAtUtc);
            Job(s);
        }

        [Fact]
        public async Task TheReasonIsOptionalAndAnUnknownOneIsRejected()
        {
            var s = Start();

            await Assert.ThrowsAsync<RequestValidationException>(() => RevokeAsync(s, (FingerprintRevocationReason)99));
            Assert.Equal(BiometricReferenceStatus.Active, s.Reference.Status);
            Assert.Empty(s.Reception.Store.Audit);

            await s.Reception.FingerprintRevocations.RevokeAsync(s.Patient.Id, s.Reference.Id, null, None);
            var revocation = Assert.Single(s.Reception.Store.Audit, a => a.EntityName == nameof(PatientBiometricReference));
            Assert.Contains("\"reason\":null", revocation.ChangesJson);
        }

        // Another patient's reference, one that is not a fingerprint, or none: 404 and nothing written.
        [Fact]
        public async Task OnlyTheFingerprintsOfThePatientCanBeRevoked()
        {
            var s = Start();
            var other = Patient.Register("Luis", "Gómez", null, BiologicalSex.Male);
            s.Reception.Store.Patients.Add(other);
            var face = PatientBiometricReference.Register(s.Patient.Id, BiometricModality.Face, "FaceProvider", "face-subject-1");
            s.Reception.Store.BiometricReferences.Add(face);

            await Assert.ThrowsAsync<ResourceNotFoundException>(() => RevokeAsync(s, patientId: other.Id));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => RevokeAsync(s, referenceId: face.Id));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => RevokeAsync(s, referenceId: Guid.NewGuid()));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => RevokeAsync(s, patientId: Guid.NewGuid()));

            Assert.All(s.Reception.Store.BiometricReferences, r => Assert.Equal(BiometricReferenceStatus.Active, r.Status));
            Assert.Empty(s.Reception.Store.Deletions);
            Assert.Empty(s.Reception.Store.Audit);
        }

        // Biometrics.Manage alone is not enough: active clinical staff who can see the patient. No role bypass.
        [Theory]
        [InlineData(SecurityRoles.Doctor)]
        [InlineData(SecurityRoles.Nurse)]
        [InlineData(SecurityRoles.Paramedic)]
        public async Task OnlyStaffWithBiometricsManageCanRevoke(string role)
        {
            var s = Start();
            var other = new ClinicalHarness(FakeCurrentUser.InRole(role), s.Reception.Store);
            other.Staff.Add(other.User.UserId!.Value, true, Guid.NewGuid());

            await Assert.ThrowsAsync<AccessDeniedException>(() => other.FingerprintRevocations.RevokeAsync(s.Patient.Id, s.Reference.Id, null, None));

            Assert.Equal(BiometricReferenceStatus.Active, s.Reference.Status);
        }

        [Fact]
        public async Task AnAdministratorWithoutClinicalStaffCannotRevoke()
        {
            var s = Start();
            var admin = new ClinicalHarness(FakeCurrentUser.InRole(SecurityRoles.Administrator), s.Reception.Store);
            admin.Time.Now = s.T0;

            await Assert.ThrowsAsync<AccessDeniedException>(() => admin.FingerprintRevocations.RevokeAsync(s.Patient.Id, s.Reference.Id, null, None));
            Assert.Equal(BiometricReferenceStatus.Active, s.Reference.Status);

            admin.Staff.Add(admin.User.UserId!.Value, true, Guid.NewGuid());
            Assert.Equal(BiometricReferenceStatus.Revoked, (await admin.FingerprintRevocations.RevokeAsync(s.Patient.Id, s.Reference.Id, null, None)).Status);
        }

        // Physically still on the sensor, the slot matches, but no ACTIVE reference: NoMatch, like an unknown slot.
        [Fact]
        public async Task ARevokedSlotThatStillMatchesOnTheSensorIsNoMatch()
        {
            var s = Start();
            await RevokeAsync(s);
            var paramedic = new ClinicalHarness(FakeCurrentUser.InRole(SecurityRoles.Paramedic), s.Reception.Store);
            paramedic.Staff.Add(paramedic.User.UserId!.Value, true, Guid.NewGuid());
            paramedic.Readers.Devices.Add(s.Reader);
            var opened = await paramedic.Emergencies.OpenAsync(
                new OpenPrehospitalEmergencyRequest(Guid.NewGuid(), ClinicalHarness.Start.AddMinutes(-5), "Inconsciente"), None);
            var session = await paramedic.Fingerprints.OpenSessionAsync(opened.Emergency.Id, new OpenFingerprintSessionRequest(Guid.NewGuid(), s.Reader.Id), None);

            await paramedic.ReaderSide.SubmitResultAsync(
                s.Reader.Id, s.Reader.DeviceCode, session.Session.Id, new SubmitFingerprintResultRequest(FingerprintCaptureOutcome.Match, Slot), None);

            var resolved = await paramedic.Fingerprints.GetSessionAsync(opened.Emergency.Id, session.Session.Id, None);
            Assert.Equal((FingerprintSessionStatus.NoMatch, (FingerprintCandidate?)null), (resolved.Status, resolved.Candidate));
            var attempt = Assert.Single(s.Reception.Store.Attempts);
            Assert.Equal((IdentificationFailureReason.NoMatch, (Guid?)null, (Guid?)null), (attempt.FailureReason, attempt.BiometricReferenceId, attempt.PatientId));
        }

        // A reference whose reader and slot cannot be known is revoked anyway (security first) without any job for a reader;
        // a reference revoked before revocation existed is history (NotRequired) and a replay never repairs it with a job.
        [Fact]
        public async Task ALegacyReferenceIsRevokedWithoutAJobAndHistoryIsNeverRepaired()
        {
            var s = Start();
            var legacy = Register(s.Reception, s.Patient, "legacy-reader-7:slot:3");

            var revoked = await RevokeAsync(s, referenceId: legacy.Id);

            Assert.Equal((BiometricReferenceStatus.Revoked, (FingerprintCleanupStatus?)FingerprintCleanupStatus.AttentionRequired), (revoked.Status, revoked.Cleanup));
            Assert.Empty(s.Reception.Store.Deletions);
            Assert.Single(s.Reception.Store.Audit);

            s.Reference.Revoke(s.T0);
            var history = await RevokeAsync(s);
            Assert.Equal(FingerprintCleanupStatus.NotRequired, history.Cleanup);
            Assert.Empty(s.Reception.Store.Deletions);
            Assert.Single(s.Reception.Store.Audit);
        }

        // The reader sees its oldest job with the slot taken from the reference; another reader sees nothing.
        [Fact]
        public async Task TheReaderGetsOnlyItsJobAndTheSlotComesFromTheReference()
        {
            var s = Start();
            var other = s.Reception.Readers.Add("ESP32-HOSP-02", DeviceCredentialStatus.Active, DeviceScopes.FingerprintEnroll, DeviceScopes.FingerprintIdentify);
            var later = Register(s.Reception, s.Patient, FingerprintReferences.SubjectReference(s.Reader.Id, 9));
            await RevokeAsync(s);
            s.Reception.Time.Now = s.T0.AddMinutes(1);
            await RevokeAsync(s, referenceId: later.Id);

            var pending = await s.Reception.DeletionReaderSide.GetPendingAsync(s.Reader.Id, None);

            Assert.Equal(new PendingFingerprintSlotDeletion(s.Reception.Store.Deletions.Single(d => d.BiometricReferenceId == s.Reference.Id).Id, Slot), pending);
            Assert.Null(await s.Reception.DeletionReaderSide.GetPendingAsync(other.Id, None));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => ReportAsync(s, pending!.DeletionId, Guid.NewGuid(), FingerprintSlotDeletionResultOutcome.Deleted, other));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => ReportAsync(s, Guid.NewGuid(), Guid.NewGuid(), FingerprintSlotDeletionResultOutcome.Deleted));
            await Assert.ThrowsAsync<RequestValidationException>(() => ReportAsync(s, pending!.DeletionId, Guid.Empty, FingerprintSlotDeletionResultOutcome.Deleted));
            await Assert.ThrowsAsync<RequestValidationException>(() => ReportAsync(s, pending!.DeletionId, Guid.NewGuid(), (FingerprintSlotDeletionResultOutcome)0));
            Assert.All(s.Reception.Store.Deletions, d => Assert.Equal((FingerprintSlotDeletionStatus.Pending, 0), (d.Status, d.Attempts)));
        }

        // attemptId: a resent attempt (a lost 204) never counts twice; only a new physical attempt does. Only the first
        // sensor error is audited. Deleted completes the job, and its outcome never changes afterwards.
        [Fact]
        public async Task EachPhysicalAttemptCountsOnceAndACompletedJobNeverChanges()
        {
            var s = Start();
            await RevokeAsync(s);
            var id = Job(s).Id;
            var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            int Audits() => s.Reception.Store.Audit.Count(x => x.EntityName == FingerprintRevocationService.DeletionEntityName && x.Action == AuditActionType.Update);

            await ReportAsync(s, id, a, FingerprintSlotDeletionResultOutcome.SensorError);
            await ReportAsync(s, id, a, FingerprintSlotDeletionResultOutcome.SensorError);
            Assert.Equal((FingerprintSlotDeletionStatus.Pending, 1, (FingerprintSlotDeletionFailure?)FingerprintSlotDeletionFailure.SensorError, 1),
                (Job(s).Status, Job(s).Attempts, Job(s).LastFailure, Audits()));
            await Assert.ThrowsAsync<ResourceConflictException>(() => ReportAsync(s, id, a, FingerprintSlotDeletionResultOutcome.Deleted));

            s.Reception.Time.Now = s.T0.AddMinutes(1);
            await ReportAsync(s, id, b, FingerprintSlotDeletionResultOutcome.SensorError);
            Assert.Equal((2, s.T0.AddMinutes(1), 1), (Job(s).Attempts, Job(s).LastAttemptAtUtc, Audits()));

            await ReportAsync(s, id, c, FingerprintSlotDeletionResultOutcome.Deleted);
            await ReportAsync(s, id, c, FingerprintSlotDeletionResultOutcome.Deleted);
            Assert.Equal((FingerprintSlotDeletionStatus.Completed, (FingerprintSlotDeletionOutcome?)FingerprintSlotDeletionOutcome.Deleted, 3, (DateTimeOffset?)s.T0.AddMinutes(1), 2),
                (Job(s).Status, Job(s).Outcome, Job(s).Attempts, Job(s).CompletedAtUtc, Audits()));
            await Assert.ThrowsAsync<ResourceConflictException>(() => ReportAsync(s, id, c, FingerprintSlotDeletionResultOutcome.NotFound));
            await Assert.ThrowsAsync<ResourceConflictException>(() => ReportAsync(s, id, Guid.NewGuid(), FingerprintSlotDeletionResultOutcome.Deleted));
            await Assert.ThrowsAsync<ResourceConflictException>(() => ReportAsync(s, id, Guid.NewGuid(), FingerprintSlotDeletionResultOutcome.SensorError));
            Assert.Equal((FingerprintSlotDeletionOutcome.Deleted, 3), (Job(s).Outcome, Job(s).Attempts));
            Assert.Null(await s.Reception.DeletionReaderSide.GetPendingAsync(s.Reader.Id, None));
            Assert.Equal(FingerprintCleanupStatus.Completed, await CleanupAsync(s));
            Assert.All(s.Reception.Store.Audit, x => Assert.DoesNotContain("slot", x.ChangesJson ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        }

        // The slot was already empty: the goal is reached as well.
        [Fact]
        public async Task NotFoundCompletesTheJob()
        {
            var s = Start();
            await RevokeAsync(s);

            await ReportAsync(s, Job(s).Id, Guid.NewGuid(), FingerprintSlotDeletionResultOutcome.NotFound);

            Assert.Equal((FingerprintSlotDeletionStatus.Completed, (FingerprintSlotDeletionOutcome?)FingerprintSlotDeletionOutcome.NotFound), (Job(s).Status, Job(s).Outcome));
            Assert.Equal(FingerprintCleanupStatus.Completed, await CleanupAsync(s));
        }

        // AttentionRequired is derived: a reader that keeps failing (it still gets the job), a revoked reader, or one that
        // cannot delete (Identify only). The job itself stays Pending.
        [Fact]
        public async Task CleanupNeedsAttentionWhenTheReaderCannotDoIt()
        {
            var s = Start();
            await RevokeAsync(s);
            for (var attempt = 1; attempt < PatientFingerprints.AttentionAttempts; attempt++)
            {
                await ReportAsync(s, Job(s).Id, Guid.NewGuid(), FingerprintSlotDeletionResultOutcome.SensorError);
            }

            Assert.Equal(FingerprintCleanupStatus.Pending, await CleanupAsync(s));
            await ReportAsync(s, Job(s).Id, Guid.NewGuid(), FingerprintSlotDeletionResultOutcome.SensorError);
            Assert.Equal(FingerprintCleanupStatus.AttentionRequired, await CleanupAsync(s));
            Assert.NotNull(await s.Reception.DeletionReaderSide.GetPendingAsync(s.Reader.Id, None));

            foreach (var reader in new[]
            {
                s.Reader with { Status = DeviceCredentialStatus.Revoked },
                s.Reader with { Scopes = [DeviceScopes.FingerprintIdentify] }
            })
            {
                var fresh = Start();
                await RevokeAsync(fresh);
                fresh.Reception.Readers.Devices[0] = reader with { Id = fresh.Reader.Id };
                Assert.Equal(FingerprintCleanupStatus.AttentionRequired, await CleanupAsync(fresh));
                Assert.Equal(FingerprintSlotDeletionStatus.Pending, Job(fresh).Status);
                Assert.Equal(BiometricReferenceStatus.Revoked, fresh.Reference.Status);
            }

            var unknown = Start();
            await RevokeAsync(unknown);
            unknown.Reception.Readers.Devices.Clear();
            Assert.Equal(FingerprintCleanupStatus.AttentionRequired, await CleanupAsync(unknown));
        }

        // P0: the template of a revoked reference is still in its slot until the reader deletes it. Enrolling another patient
        // in that slot meanwhile would let the deletion erase the new template: 409, the enrollment stays Pending and another
        // slot works. Once the deletion is completed the slot can be enrolled again.
        [Fact]
        public async Task ASlotWaitingForItsDeletionCannotBeEnrolledUntilTheDeletionCompletes()
        {
            var s = Start();
            await RevokeAsync(s);
            var other = Patient.Register("Luis", "Gómez", null, BiologicalSex.Male);
            s.Reception.Store.Patients.Add(other);
            var enrollment = (await s.Reception.FingerprintEnrollments.OpenAsync(other.Id, new OpenFingerprintEnrollmentRequest(Guid.NewGuid(), s.Reader.Id), None)).Enrollment;

            await Assert.ThrowsAsync<ResourceConflictException>(() => s.Reception.EnrollmentReaderSide.SubmitResultAsync(
                s.Reader.Id, s.Reader.DeviceCode, enrollment.Id, new SubmitFingerprintEnrollmentResultRequest(FingerprintEnrollmentOutcome.Enrolled, Slot), None));
            Assert.Equal(FingerprintEnrollmentStatus.Pending, s.Reception.Store.Enrollments.Single(e => e.Id == enrollment.Id).Status);
            await s.Reception.EnrollmentReaderSide.SubmitResultAsync(
                s.Reader.Id, s.Reader.DeviceCode, enrollment.Id, new SubmitFingerprintEnrollmentResultRequest(FingerprintEnrollmentOutcome.Enrolled, Slot + 1), None);

            await ReportAsync(s, Job(s).Id, Guid.NewGuid(), FingerprintSlotDeletionResultOutcome.Deleted);
            var third = Patient.Register("Eva", "Ruiz", null, BiologicalSex.Female);
            s.Reception.Store.Patients.Add(third);
            var again = (await s.Reception.FingerprintEnrollments.OpenAsync(third.Id, new OpenFingerprintEnrollmentRequest(Guid.NewGuid(), s.Reader.Id), None)).Enrollment;
            await s.Reception.EnrollmentReaderSide.SubmitResultAsync(
                s.Reader.Id, s.Reader.DeviceCode, again.Id, new SubmitFingerprintEnrollmentResultRequest(FingerprintEnrollmentOutcome.Enrolled, Slot), None);

            Assert.Equal(third.Id, s.Reception.Store.BiometricReferences.Single(r => r.Status == BiometricReferenceStatus.Active
                && r.SubjectReference == FingerprintReferences.SubjectReference(s.Reader.Id, Slot)).PatientId);
        }

        // The pending deletion of a slot on ANOTHER reader does not block this reader's slot with the same number.
        [Fact]
        public async Task OnlyTheSameSlotOfTheSameReaderIsBlocked()
        {
            var s = Start();
            await RevokeAsync(s);
            var reader2 = s.Reception.Readers.Add("ESP32-HOSP-02", DeviceCredentialStatus.Active, DeviceScopes.FingerprintEnroll, DeviceScopes.FingerprintIdentify);
            var enrollment = (await s.Reception.FingerprintEnrollments.OpenAsync(s.Patient.Id, new OpenFingerprintEnrollmentRequest(Guid.NewGuid(), reader2.Id), None)).Enrollment;

            await s.Reception.EnrollmentReaderSide.SubmitResultAsync(
                reader2.Id, reader2.DeviceCode, enrollment.Id, new SubmitFingerprintEnrollmentResultRequest(FingerprintEnrollmentOutcome.Enrolled, Slot), None);

            Assert.Equal(FingerprintEnrollmentStatus.Enrolled, s.Reception.Store.Enrollments.Single().Status);
        }
    }

    // The only parser of the prototype's subject reference: exact format, invariant, round-trip.
    public class FingerprintReferencesTests
    {
        private static readonly Guid Reader = Guid.Parse("4a1d2f6e-7b8c-4d9e-a0b1-c2d3e4f5a6b7");

        [Theory]
        [InlineData(0)]
        [InlineData(5)]
        [InlineData(int.MaxValue)]
        public void AReferenceBuiltHereIsParsedBack(int slot)
        {
            Assert.True(FingerprintReferences.TryParse(FingerprintReferences.SubjectReference(Reader, slot), out var reader, out var parsed));
            Assert.Equal((Reader, slot), (reader, parsed));
            Assert.Equal(Reader, FingerprintReferences.ReaderOf(FingerprintReferences.SubjectReference(Reader, slot)));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("r307:device:4a1d2f6e-7b8c-4d9e-a0b1-c2d3e4f5a6b7")]
        [InlineData("r307:device:4a1d2f6e-7b8c-4d9e-a0b1-c2d3e4f5a6b7:slot:")]
        [InlineData("r307:device:4a1d2f6e-7b8c-4d9e-a0b1-c2d3e4f5a6b7:slot:-1")]
        [InlineData("r307:device:4a1d2f6e-7b8c-4d9e-a0b1-c2d3e4f5a6b7:slot:+1")]
        [InlineData("r307:device:4a1d2f6e-7b8c-4d9e-a0b1-c2d3e4f5a6b7:slot:01")]
        [InlineData("r307:device:4a1d2f6e-7b8c-4d9e-a0b1-c2d3e4f5a6b7:slot: 1")]
        [InlineData("r307:device:4a1d2f6e-7b8c-4d9e-a0b1-c2d3e4f5a6b7:slot:1 ")]
        [InlineData("r307:device:4a1d2f6e-7b8c-4d9e-a0b1-c2d3e4f5a6b7:slot:2147483648")]
        [InlineData("r307:device:4A1D2F6E-7B8C-4D9E-A0B1-C2D3E4F5A6B7:slot:1")]
        [InlineData("r307:device:{4a1d2f6e-7b8c-4d9e-a0b1-c2d3e4f5a6b7}:slot:1")]
        [InlineData("r307:device:4a1d2f6e7b8c4d9ea0b1c2d3e4f5a6b7:slot:1")]
        [InlineData("R307:device:4a1d2f6e-7b8c-4d9e-a0b1-c2d3e4f5a6b7:slot:1")]
        [InlineData("r307:device:4a1d2f6e-7b8c-4d9e-a0b1-c2d3e4f5a6b7:slot:1:slot:2")]
        [InlineData("legacy-reader-7:slot:3")]
        public void AnythingElseIsNotAReference(string? subjectReference)
        {
            Assert.False(FingerprintReferences.TryParse(subjectReference, out _, out _));
            if (subjectReference is not null)
            {
                Assert.Null(FingerprintReferences.ReaderOf(subjectReference));
            }
        }
    }
}
