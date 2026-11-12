using SCI_PH.Application.Abstractions.Biometrics;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Biometrics;
using SCI_PH.Application.Features.Clinical;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Patients;

namespace SCI_PH.Application.Tests.Clinical
{
    // Assisted 1:1 face verification (PatientFaceService, ADR-012) over the in-memory store and a recording provider:
    // enrollment and its compensation, verification against the patient's own active reference only, idempotency that
    // never calls the provider twice, revocation and its durable template cleanup, and no identification of anything.
    public class FaceVerificationTests
    {
        private static readonly CancellationToken None = CancellationToken.None;

        private sealed record Scenario(ClinicalHarness Reception, Patient Patient);

        private static Scenario Start()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Receptionist);
            harness.Time.Now = DateTimeOffset.UtcNow.AddMinutes(1);
            var patient = Patient.Register("Ana", "Pérez", new DateOnly(1985, 3, 14), BiologicalSex.Female);
            harness.Store.Patients.Add(patient);
            return new Scenario(harness, patient);
        }

        private static FaceCaptureUpload Upload(string content, Guid? key = null) =>
            new(key ?? Guid.NewGuid(), TestCaptures.Of(content), System.Text.Encoding.UTF8.GetByteCount(content));

        private static Task<EnrollFaceResult> EnrollAsync(Scenario s, string face = "face:A", Guid? key = null, ClinicalHarness? by = null) =>
            (by ?? s.Reception).Faces.EnrollAsync(s.Patient.Id, Upload(face, key), None);

        private static Task<FaceVerificationResponse> VerifyAsync(Scenario s, string face, Guid? key = null, ClinicalHarness? by = null) =>
            (by ?? s.Reception).Faces.VerifyAsync(s.Patient.Id, Upload(face, key), None);

        private static PatientBiometricReference ActiveFace(Scenario s) =>
            s.Reception.Store.BiometricReferences.Single(r => r.Modality == BiometricModality.Face && r.Status == BiometricReferenceStatus.Active);

        [Fact]
        public async Task AnEnrolledFaceIsAnOpaqueProviderReferenceWithItsAudit()
        {
            var s = Start();

            var result = await EnrollAsync(s);

            Assert.True(result.Created);
            Assert.Equal(FaceEnrollmentOutcome.Enrolled, result.Response.Outcome);
            var reference = ActiveFace(s);
            Assert.Equal((s.Patient.Id, "TEST-Face", "test-v1", (Guid?)s.Reception.StaffId), (reference.PatientId, reference.Provider, reference.AlgorithmVersion, reference.RegisteredByStaffId));
            Assert.StartsWith("face:", reference.SubjectReference);
            Assert.Equal(new PatientFaceSummary(reference.Id, BiometricReferenceStatus.Active, "TEST-Face", reference.CreatedAtUtc, null), result.Response.Face);
            var audit = Assert.Single(s.Reception.Store.Audit);
            Assert.Equal((AuditActionType.Create, (Guid?)s.Patient.Id), (audit.Action, audit.PatientId));
            Assert.Contains("test-v1", audit.ChangesJson);
            Assert.DoesNotContain("face:", audit.ChangesJson);
        }

        // A replay answers without calling the provider; the same key with another capture is 409; a second active face is
        // 409 before the provider is called.
        [Fact]
        public async Task EnrollmentIsIdempotentAndASecondActiveFaceIsRejected()
        {
            var s = Start();
            var key = Guid.NewGuid();
            var first = await EnrollAsync(s, key: key);

            var replay = await EnrollAsync(s, key: key);

            Assert.False(replay.Created);
            Assert.Equal(first.Response, replay.Response);
            Assert.Single(s.Reception.FaceProvider.Calls);
            await Assert.ThrowsAsync<ResourceConflictException>(() => EnrollAsync(s, "face:B", key));
            await Assert.ThrowsAsync<ResourceConflictException>(() => EnrollAsync(s, "face:B"));
            Assert.Single(s.Reception.FaceProvider.Calls);
            Assert.Single(s.Reception.Store.BiometricReferences);
            Assert.Single(s.Reception.Store.Audit);
        }

        [Theory]
        [InlineData("noface", FaceEnrollmentOutcome.NoFace)]
        [InlineData("multiple", FaceEnrollmentOutcome.MultipleFaces)]
        [InlineData("lowquality", FaceEnrollmentOutcome.LowQuality)]
        public async Task ACaptureWithoutAUsableFaceCreatesNothing(string face, FaceEnrollmentOutcome outcome)
        {
            var s = Start();

            var result = await EnrollAsync(s, face);

            Assert.Equal((outcome, (PatientFaceSummary?)null, false), (result.Response.Outcome, result.Response.Face, result.Created));
            Assert.Empty(s.Reception.Store.BiometricReferences);
            Assert.Empty(s.Reception.Store.Audit);
            Assert.Empty(s.Reception.FaceProvider.Subjects);
        }

        // D13: the provider enrolled, the clinical commit failed: its subject is deleted and the original error stays.
        [Fact]
        public async Task AFailedClinicalCommitDeletesTheProvidersSubject()
        {
            var s = Start();
            s.Reception.Store.BeforeSave = () => throw new ResourceConflictException("The patient already has an active face reference.");

            var error = await Assert.ThrowsAsync<ResourceConflictException>(() => EnrollAsync(s));

            Assert.Equal("The patient already has an active face reference.", error.Message);
            Assert.Empty(s.Reception.FaceProvider.Subjects);
            Assert.Equal(2, s.Reception.FaceProvider.Calls.Count);
            Assert.Equal("enroll", s.Reception.FaceProvider.Calls[0]);
            Assert.StartsWith("delete:face:", s.Reception.FaceProvider.Calls[1]);
            Assert.Empty(s.Reception.Store.BiometricReferences);
        }

        // 1:1 only against the patient's own active reference; every outcome is audited (never a score); nothing clinical
        // changes and nothing is identified.
        [Fact]
        public async Task VerificationComparesOnlyWithThePatientsActiveFace()
        {
            var s = Start();
            await EnrollAsync(s);
            var subject = ActiveFace(s).SubjectReference;

            Assert.Equal(FaceVerificationOutcome.Match, (await VerifyAsync(s, "face:A")).Outcome);
            Assert.Equal(FaceVerificationOutcome.NoMatch, (await VerifyAsync(s, "face:B")).Outcome);
            Assert.Equal(FaceVerificationOutcome.NoFace, (await VerifyAsync(s, "noface")).Outcome);
            Assert.Equal(FaceVerificationOutcome.MultipleFaces, (await VerifyAsync(s, "multiple")).Outcome);
            Assert.Equal(FaceVerificationOutcome.LowQuality, (await VerifyAsync(s, "lowquality")).Outcome);

            Assert.All(s.Reception.FaceProvider.Calls.Skip(1), call => Assert.Equal($"verify:{subject}", call));
            var verifications = s.Reception.Store.Audit.Where(a => a.EntityName == PatientFaceService.VerificationEntityName).ToList();
            Assert.Equal(5, verifications.Count);
            Assert.All(verifications, a =>
            {
                Assert.Equal((AuditActionType.Read, (Guid?)s.Patient.Id, (Guid?)null), (a.Action, a.PatientId, a.ClinicalEncounterId));
                Assert.DoesNotContain("face:", a.ChangesJson);
                Assert.DoesNotContain("score", a.ChangesJson, StringComparison.OrdinalIgnoreCase);
            });
            Assert.Empty(s.Reception.Store.Attempts);
            Assert.Empty(s.Reception.Store.Encounters);
        }

        // D19: the replay returns the recorded outcome without calling the provider; another capture with the key is 409.
        [Fact]
        public async Task AReplayedVerificationNeverCallsTheProviderAgain()
        {
            var s = Start();
            await EnrollAsync(s);
            var key = Guid.NewGuid();
            var first = await VerifyAsync(s, "face:B", key);
            var calls = s.Reception.FaceProvider.Calls.Count;

            var replay = await VerifyAsync(s, "face:B", key);

            Assert.Equal(first, replay);
            Assert.Equal(calls, s.Reception.FaceProvider.Calls.Count);
            await Assert.ThrowsAsync<ResourceConflictException>(() => VerifyAsync(s, "face:A", key));
        }

        [Fact]
        public async Task WithoutAnActiveFaceThereIsNothingToVerify()
        {
            var s = Start();

            await Assert.ThrowsAsync<ResourceConflictException>(() => VerifyAsync(s, "face:A"));
            Assert.Empty(s.Reception.FaceProvider.Calls);
        }

        // Ownership: an active face of another provider, or of another algorithm version of this one, is never handed to the
        // provider; it is a 503 (re-enrollment needed), not a "no face" and never a NoMatch.
        [Fact]
        public async Task AFaceOfAnotherProviderOrVersionIsNeverInterpreted()
        {
            var s = Start();
            var other = PatientBiometricReference.Register(s.Patient.Id, BiometricModality.Face, "OTHER", "face:x", algorithmVersion: "test-v1");
            s.Reception.Store.BiometricReferences.Add(other);
            await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() => VerifyAsync(s, "face:A"));

            s.Reception.Store.BiometricReferences.Remove(other);
            s.Reception.Store.BiometricReferences.Add(
                PatientBiometricReference.Register(s.Patient.Id, BiometricModality.Face, "TEST-Face", "face:y", algorithmVersion: "test-v0"));
            await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() => VerifyAsync(s, "face:A"));

            Assert.Empty(s.Reception.FaceProvider.Calls);
            Assert.DoesNotContain(s.Reception.Store.Audit, a => a.EntityName == PatientFaceService.VerificationEntityName);
        }

        [Fact]
        public async Task AFailingProviderIsUnavailableAndRecordsNothing()
        {
            var s = Start();
            await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() => EnrollAsync(s, "down"));
            await EnrollAsync(s);

            await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() => VerifyAsync(s, "down"));

            Assert.DoesNotContain(s.Reception.Store.Audit, a => a.EntityName == PatientFaceService.VerificationEntityName);
        }

        // Revocation is logical first and commits ONE Pending cleanup job with it; the immediate attempt completes it and audits
        // only that completion (by the system); a replay writes nothing; a new face can be enrolled afterwards.
        [Fact]
        public async Task RevocationCommitsADurableCleanupThatCompletesOnce()
        {
            var s = Start();
            await EnrollAsync(s);
            var reference = ActiveFace(s);

            var revoked = await s.Reception.Faces.RevokeAsync(s.Patient.Id, reference.Id, new RevokeFaceRequest(FaceRevocationReason.PatientRequest), None);

            Assert.Equal(BiometricReferenceStatus.Revoked, revoked.Status);
            var job = Assert.Single(s.Reception.Store.FaceDeletions);
            Assert.Equal(reference.Id, job.BiometricReferenceId);
            Assert.Equal(FaceTemplateDeletionStatus.Completed, job.Status);
            Assert.Equal(FaceTemplateDeletionOutcome.Deleted, job.Outcome);
            Assert.Empty(s.Reception.FaceProvider.Subjects);
            var updates = s.Reception.Store.Audit.Where(a => a.Action == AuditActionType.Update).ToList();
            Assert.Equal(2, updates.Count);
            Assert.Contains("PatientRequest", updates[0].ChangesJson);
            Assert.Contains("\"cleanup\":\"Deleted\"", updates[1].ChangesJson);
            Assert.Equal(AuditActorType.System, updates[1].ActorType);
            await Assert.ThrowsAsync<ResourceConflictException>(() => VerifyAsync(s, "face:A"));

            var audits = s.Reception.Store.Audit.Count;
            Assert.Equal(revoked, await s.Reception.Faces.RevokeAsync(s.Patient.Id, reference.Id, null, None));
            Assert.Equal(audits, s.Reception.Store.Audit.Count);
            Assert.Single(s.Reception.Store.FaceDeletions);

            Assert.True((await EnrollAsync(s, "face:B")).Created);
            Assert.Equal(FaceVerificationOutcome.Match, (await VerifyAsync(s, "face:B")).Outcome);
        }

        // A provider outage never undoes or fails the revocation: the job stays Pending with its failed attempt, and the
        // worker passes retry it after 1, 2, 3, 5 then 10 minutes until it completes; nothing but the completion is audited.
        [Fact]
        public async Task AFailedCleanupStaysPendingAndIsRetriedWithBackoff()
        {
            var s = Start();
            await EnrollAsync(s);
            var reference = ActiveFace(s);
            var provider = s.Reception.FaceProvider;
            provider.FailDeletes = true;

            var revoked = await s.Reception.Faces.RevokeAsync(s.Patient.Id, reference.Id, null, None);

            Assert.Equal(BiometricReferenceStatus.Revoked, revoked.Status);
            var job = Assert.Single(s.Reception.Store.FaceDeletions);
            Assert.Equal((FaceTemplateDeletionStatus.Pending, 1, FaceTemplateDeletionFailure.ProviderUnavailable),
                (job.Status, job.Attempts, job.LastFailure));
            var audits = s.Reception.Store.Audit.Count;

            // Not due yet: no call.
            var calls = provider.Calls.Count;
            Assert.Equal(0, await s.Reception.FaceCleanup.RunOnceAsync(10, None));
            Assert.Equal(calls, provider.Calls.Count);

            foreach (var (wait, attempts) in new[] { (1, 2), (2, 3), (3, 4), (5, 5), (10, 6), (10, 7) })
            {
                s.Reception.Time.Now = s.Reception.Time.Now.AddMinutes(wait).AddSeconds(-1);
                Assert.Equal(0, await s.Reception.FaceCleanup.RunOnceAsync(10, None));
                Assert.Equal(attempts - 1, s.Reception.Store.FaceDeletions.Single().Attempts);
                s.Reception.Time.Now = s.Reception.Time.Now.AddSeconds(1);
                Assert.Equal(0, await s.Reception.FaceCleanup.RunOnceAsync(10, None));
                Assert.Equal(attempts, s.Reception.Store.FaceDeletions.Single().Attempts);
            }

            Assert.True(s.Reception.Store.FaceDeletions.Single().Attempts >= FaceTemplateCleanupService.AttentionAttempts);
            Assert.Equal(audits, s.Reception.Store.Audit.Count);

            provider.FailDeletes = false;
            s.Reception.Time.Now = s.Reception.Time.Now.AddMinutes(10);
            Assert.Equal(1, await s.Reception.FaceCleanup.RunOnceAsync(10, None));
            job = s.Reception.Store.FaceDeletions.Single();
            Assert.Equal((FaceTemplateDeletionStatus.Completed, FaceTemplateDeletionOutcome.Deleted), (job.Status, job.Outcome));
            Assert.Equal(audits + 1, s.Reception.Store.Audit.Count);
            Assert.Equal(0, await s.Reception.FaceCleanup.RunOnceAsync(10, None));
        }

        // No provider configured (Disabled): the revocation stands and its job stays Pending as ProviderDisabled, but nothing is
        // sent to the provider and no attempt is counted, neither now nor on later worker passes.
        [Fact]
        public async Task WithTheProviderDisabledTheCleanupWaitsWithoutCountingAttempts()
        {
            var s = Start();
            await EnrollAsync(s);
            var reference = ActiveFace(s);
            s.Reception.FaceProvider.IsEnabled = false;
            var saves = s.Reception.Store.SaveCount;

            var revoked = await s.Reception.Faces.RevokeAsync(s.Patient.Id, reference.Id, null, None);

            Assert.Equal(BiometricReferenceStatus.Revoked, revoked.Status);
            Assert.Equal(saves + 1, s.Reception.Store.SaveCount);
            var job = Assert.Single(s.Reception.Store.FaceDeletions);
            Assert.Equal((FaceTemplateDeletionStatus.Pending, 0, FaceTemplateDeletionFailure.ProviderDisabled, (DateTimeOffset?)null),
                (job.Status, job.Attempts, job.LastFailure, job.LastAttemptAtUtc));

            s.Reception.Time.Now = s.Reception.Time.Now.AddHours(1);
            Assert.Equal(0, await s.Reception.FaceCleanup.RunOnceAsync(10, None));
            Assert.False(await s.Reception.FaceCleanup.TryCompleteAsync(job.Id, None));
            Assert.Equal(job, Assert.Single(s.Reception.Store.FaceDeletions));
            Assert.Equal(saves + 1, s.Reception.Store.SaveCount);
            Assert.DoesNotContain(s.Reception.FaceProvider.Calls, c => c.StartsWith("delete", StringComparison.Ordinal));

            // Once a provider is configured again, the job is processed like any other.
            s.Reception.FaceProvider.IsEnabled = true;
            Assert.Equal(1, await s.Reception.FaceCleanup.RunOnceAsync(10, None));
            Assert.Equal(FaceTemplateDeletionStatus.Completed, s.Reception.Store.FaceDeletions.Single().Status);
        }

        // A template that is already gone completes the job as NotFound.
        [Fact]
        public async Task AMissingTemplateCompletesTheCleanupAsNotFound()
        {
            var s = Start();
            await EnrollAsync(s);
            var reference = ActiveFace(s);
            s.Reception.FaceProvider.Subjects.Clear();

            await s.Reception.Faces.RevokeAsync(s.Patient.Id, reference.Id, null, None);

            var job = Assert.Single(s.Reception.Store.FaceDeletions);
            Assert.Equal((FaceTemplateDeletionStatus.Completed, FaceTemplateDeletionOutcome.NotFound), (job.Status, job.Outcome));
        }

        // The cleanup never sends another provider's subject reference to the configured one: the job stays Pending.
        [Fact]
        public async Task TheCleanupNeverInterpretsAnotherProvidersReference()
        {
            var s = Start();
            var other = PatientBiometricReference.Register(s.Patient.Id, BiometricModality.Face, "OTHER", "face:x", algorithmVersion: "v");
            s.Reception.Store.BiometricReferences.Add(other);

            await s.Reception.Faces.RevokeAsync(s.Patient.Id, other.Id, null, None);

            var job = Assert.Single(s.Reception.Store.FaceDeletions);
            Assert.Equal((FaceTemplateDeletionStatus.Pending, FaceTemplateDeletionFailure.ProviderMismatch), (job.Status, job.LastFailure));
            Assert.DoesNotContain(s.Reception.FaceProvider.Calls, c => c.StartsWith("delete", StringComparison.Ordinal));
        }

        // Two workers on the same job: both may ask the provider, only the first result applies and the loser audits nothing.
        [Fact]
        public async Task TwoWorkersConvergeOnOneCompletedJob()
        {
            var s = Start();
            await EnrollAsync(s);
            var reference = ActiveFace(s);
            s.Reception.FaceProvider.FailDeletes = true;
            await s.Reception.Faces.RevokeAsync(s.Patient.Id, reference.Id, null, None);
            s.Reception.FaceProvider.FailDeletes = false;
            s.Reception.Time.Now = s.Reception.Time.Now.AddMinutes(1);
            var audits = s.Reception.Store.Audit.Count;

            // Another worker commits its completion while this one is between its read and its save.
            s.Reception.Store.BeforeSave = () =>
            {
                s.Reception.Store.BeforeSave = null;
                var stored = s.Reception.Store.FaceDeletions[0];
                s.Reception.Store.FaceDeletions[0] = stored with
                {
                    Status = FaceTemplateDeletionStatus.Completed, Outcome = FaceTemplateDeletionOutcome.NotFound, CompletedAtUtc = s.Reception.Time.Now
                };
            };

            Assert.Equal(0, await s.Reception.FaceCleanup.RunOnceAsync(10, None));

            var job = Assert.Single(s.Reception.Store.FaceDeletions);
            Assert.Equal((FaceTemplateDeletionStatus.Completed, FaceTemplateDeletionOutcome.NotFound), (job.Status, job.Outcome));
            Assert.Equal(audits, s.Reception.Store.Audit.Count);
        }

        [Fact]
        public async Task OnlyFacesOfThePatientCanBeRevokedWithAKnownReason()
        {
            var s = Start();
            await EnrollAsync(s);
            var reference = ActiveFace(s);
            var fingerprint = PatientBiometricReference.Register(s.Patient.Id, BiometricModality.Fingerprint, "R307", "r307:x");
            s.Reception.Store.BiometricReferences.Add(fingerprint);
            var other = Patient.Register("Luis", "Gómez", null, BiologicalSex.Male);
            s.Reception.Store.Patients.Add(other);

            await Assert.ThrowsAsync<ResourceNotFoundException>(() => s.Reception.Faces.RevokeAsync(s.Patient.Id, fingerprint.Id, null, None));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => s.Reception.Faces.RevokeAsync(other.Id, reference.Id, null, None));
            await Assert.ThrowsAsync<RequestValidationException>(() =>
                s.Reception.Faces.RevokeAsync(s.Patient.Id, reference.Id, new RevokeFaceRequest((FaceRevocationReason)99), None));
            Assert.Equal(BiometricReferenceStatus.Active, reference.Status);
        }

        // Biometrics.Manage enrolls and revokes; Patients.Identify verifies; an Administrator needs a clinical actor.
        [Fact]
        public async Task PermissionsFollowTheOperation()
        {
            var s = Start();
            await EnrollAsync(s);
            var doctor = new ClinicalHarness(FakeCurrentUser.InRole(SecurityRoles.Doctor), s.Reception.Store);
            doctor.Staff.Add(doctor.User.UserId!.Value, true, Guid.NewGuid());
            doctor.FaceProvider.Subjects[ActiveFace(s).SubjectReference] = "face:A";
            var nurse = new ClinicalHarness(FakeCurrentUser.InRole(SecurityRoles.Nurse), s.Reception.Store);
            nurse.Staff.Add(nurse.User.UserId!.Value, true, Guid.NewGuid());
            var admin = new ClinicalHarness(FakeCurrentUser.InRole(SecurityRoles.Administrator), s.Reception.Store);

            Assert.Equal(FaceVerificationOutcome.Match, (await VerifyAsync(s, "face:A", by: doctor)).Outcome);
            await Assert.ThrowsAsync<AccessDeniedException>(() => EnrollAsync(s, by: doctor));
            await Assert.ThrowsAsync<AccessDeniedException>(() => VerifyAsync(s, "face:A", by: nurse));
            await Assert.ThrowsAsync<AccessDeniedException>(() => nurse.Faces.ListAsync(s.Patient.Id, None));
            await Assert.ThrowsAsync<AccessDeniedException>(() => VerifyAsync(s, "face:A", by: admin));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => s.Reception.Faces.VerifyAsync(Guid.NewGuid(), Upload("face:A"), None));
        }
    }
}
