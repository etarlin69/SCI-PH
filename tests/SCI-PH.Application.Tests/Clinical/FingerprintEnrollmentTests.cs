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
using SCI_PH.Domain.Enums.Organizations;

namespace SCI_PH.Application.Tests.Clinical
{
    // Fingerprint enrollment (FingerprintEnrollmentService for hospital staff, FingerprintEnrollmentReaderService for the
    // reader): a Pending enrollment per patient and reader for 120 s; the reader reports the slot it stored the template in
    // and only then the PatientBiometricReference exists; one active reference per patient and reader; and the confirmation
    // of an emergency identification loses against a revocation of its reference.
    public class FingerprintEnrollmentTests
    {
        private static readonly CancellationToken None = CancellationToken.None;
        private const int Slot = 4;

        private sealed record Scenario(ClinicalHarness Reception, Patient Patient, DeviceSummary Reader);

        private static Scenario Start()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Receptionist);
            var patient = Patient.Register("Ana", "Pérez", new DateOnly(1985, 3, 14), BiologicalSex.Female);
            harness.Store.Patients.Add(patient);
            var reader = harness.Readers.Add("ESP32-HOSP-01", DeviceCredentialStatus.Active, DeviceScopes.FingerprintEnroll, DeviceScopes.FingerprintIdentify);
            return new Scenario(harness, patient, reader);
        }

        private static async Task<FingerprintEnrollmentSummary> OpenAsync(Scenario s, Guid? key = null, Guid? readerId = null, Guid? patientId = null) =>
            (await s.Reception.FingerprintEnrollments.OpenAsync(
                patientId ?? s.Patient.Id, new OpenFingerprintEnrollmentRequest(key ?? Guid.NewGuid(), readerId ?? s.Reader.Id), None)).Enrollment;

        private static Task SubmitAsync(Scenario s, Guid enrollmentId, FingerprintEnrollmentOutcome outcome, int? slot = null, DeviceSummary? reader = null) =>
            s.Reception.EnrollmentReaderSide.SubmitResultAsync(
                (reader ?? s.Reader).Id, (reader ?? s.Reader).DeviceCode, enrollmentId, new SubmitFingerprintEnrollmentResultRequest(outcome, slot), None);

        private static FingerprintEnrollment Stored(Scenario s, Guid id) => s.Reception.Store.Enrollments.Single(x => x.Id == id);

        [Fact]
        public async Task OnlyActiveReadersThatCanEnrollAndIdentifyAreOffered()
        {
            var s = Start();
            s.Reception.Readers.Add("ESP32-ENROLL-ONLY", DeviceCredentialStatus.Active, DeviceScopes.FingerprintEnroll);
            s.Reception.Readers.Add("ESP32-IDENTIFY-ONLY", DeviceCredentialStatus.Active, DeviceScopes.FingerprintIdentify);
            s.Reception.Readers.Add("ESP32-REVOKED", DeviceCredentialStatus.Revoked, DeviceScopes.FingerprintEnroll, DeviceScopes.FingerprintIdentify);

            var enrollers = await s.Reception.FingerprintEnrollments.ListEnrollersAsync(None);

            Assert.Equal(s.Reader.Id, Assert.Single(enrollers).Id);
            foreach (var other in s.Reception.Readers.Devices.Where(d => d.Id != s.Reader.Id))
            {
                await Assert.ThrowsAsync<ResourceNotFoundException>(() => OpenAsync(s, readerId: other.Id));
            }
        }

        // The full enrollment: Pending for 120 s, the reader's slot creates the reference (registered by the staff member who
        // opened it), with its audit entries and without any IdentificationAttempt.
        [Fact]
        public async Task AnEnrolledSlotCreatesTheReferenceRegisteredByTheStaffMember()
        {
            var s = Start();
            var key = Guid.NewGuid();

            var opened = await s.Reception.FingerprintEnrollments.OpenAsync(s.Patient.Id, new OpenFingerprintEnrollmentRequest(key, s.Reader.Id), None);

            Assert.True(opened.Created);
            Assert.Equal((FingerprintEnrollmentStatus.Pending, ClinicalHarness.Start.AddSeconds(120)), (opened.Enrollment.Status, opened.Enrollment.ExpiresAtUtc));
            Assert.Equal(s.Reception.StaffId, Stored(s, opened.Enrollment.Id).RequestedByStaffId);
            var replay = await s.Reception.FingerprintEnrollments.OpenAsync(s.Patient.Id, new OpenFingerprintEnrollmentRequest(key, s.Reader.Id), None);
            Assert.Equal((false, opened.Enrollment), (replay.Created, replay.Enrollment));
            Assert.Equal(new PendingFingerprintEnrollment(opened.Enrollment.Id, opened.Enrollment.ExpiresAtUtc),
                await s.Reception.EnrollmentReaderSide.GetPendingAsync(s.Reader.Id, None));

            await SubmitAsync(s, opened.Enrollment.Id, FingerprintEnrollmentOutcome.Enrolled, Slot);

            var reference = Assert.Single(s.Reception.Store.BiometricReferences);
            Assert.Equal((s.Patient.Id, BiometricModality.Fingerprint, "R307", FingerprintReferences.SubjectReference(s.Reader.Id, Slot), BiometricReferenceStatus.Active, (Guid?)s.Reception.StaffId, (string?)null),
                (reference.PatientId, reference.Modality, reference.Provider, reference.SubjectReference, reference.Status, reference.RegisteredByStaffId, reference.AlgorithmVersion));
            var status = await s.Reception.FingerprintEnrollments.GetAsync(s.Patient.Id, opened.Enrollment.Id, None);
            Assert.Equal((FingerprintEnrollmentStatus.Enrolled, (Guid?)reference.Id), (status.Status, status.BiometricReferenceId));
            Assert.Empty(s.Reception.Store.Attempts);
            Assert.Single(s.Reception.Store.Audit, a => a.EntityName == FingerprintEnrollmentService.EnrollmentEntityName && a.Action == AuditActionType.Create);
            var created = Assert.Single(s.Reception.Store.Audit, a => a.EntityName == nameof(PatientBiometricReference));
            Assert.Equal((AuditActionType.Create, (Guid?)s.Patient.Id, (Guid?)null), (created.Action, created.PatientId, created.ClinicalEncounterId));
            Assert.All(s.Reception.Store.Audit, a => Assert.DoesNotContain("r307", a.ChangesJson ?? string.Empty, StringComparison.OrdinalIgnoreCase));
            Assert.Null(await s.Reception.EnrollmentReaderSide.GetPendingAsync(s.Reader.Id, None));

            var listed = Assert.Single(await s.Reception.FingerprintEnrollments.ListPatientFingerprintsAsync(s.Patient.Id, None));
            Assert.Equal((reference.Id, BiometricReferenceStatus.Active, new FingerprintReferenceReader("ESP32-HOSP-01", "Lector ESP32-HOSP-01", true)),
                (listed.Id, listed.Status, listed.Reader));
        }

        // A lost 204: the same result is accepted again without a second reference; another result is rejected.
        [Fact]
        public async Task AnIdenticalResultIsAReplayAndAChangedOneIsRejected()
        {
            var s = Start();
            var enrollment = await OpenAsync(s);
            await SubmitAsync(s, enrollment.Id, FingerprintEnrollmentOutcome.Enrolled, Slot);

            await SubmitAsync(s, enrollment.Id, FingerprintEnrollmentOutcome.Enrolled, Slot);
            await Assert.ThrowsAsync<ResourceConflictException>(() => SubmitAsync(s, enrollment.Id, FingerprintEnrollmentOutcome.Enrolled, Slot + 1));
            await Assert.ThrowsAsync<ResourceConflictException>(() => SubmitAsync(s, enrollment.Id, FingerprintEnrollmentOutcome.Failed));

            Assert.Single(s.Reception.Store.BiometricReferences);
            Assert.Equal(2, s.Reception.Store.Audit.Count);
        }

        [Theory]
        [InlineData(FingerprintEnrollmentOutcome.Failed, IdentificationFailureReason.Other)]
        [InlineData(FingerprintEnrollmentOutcome.LowQuality, IdentificationFailureReason.LowQualityCapture)]
        [InlineData(FingerprintEnrollmentOutcome.SensorError, IdentificationFailureReason.ProviderUnavailable)]
        public async Task AFailedEnrollmentIsTerminalAndANewAttemptIsANewEnrollment(FingerprintEnrollmentOutcome outcome, IdentificationFailureReason reason)
        {
            var s = Start();
            var enrollment = await OpenAsync(s);

            await SubmitAsync(s, enrollment.Id, outcome);

            var status = await s.Reception.FingerprintEnrollments.GetAsync(s.Patient.Id, enrollment.Id, None);
            Assert.Equal((FingerprintEnrollmentStatus.Failed, (FingerprintEnrollmentOutcome?)outcome, (Guid?)null), (status.Status, status.Failure, status.BiometricReferenceId));
            Assert.Equal(reason, Stored(s, enrollment.Id).FailureReason);
            Assert.Empty(s.Reception.Store.BiometricReferences);
            Assert.Equal(FingerprintEnrollmentStatus.Pending, (await OpenAsync(s)).Status);
        }

        // A slot that is already an active reference of this reader: 409, the enrollment stays Pending (the reader discards
        // its slot and may store in another one before the expiry).
        [Fact]
        public async Task AnActiveSlotIsAConflictAndTheEnrollmentWaitsForAnotherSlot()
        {
            var s = Start();
            var other = Patient.Register("Luis", "Gómez", null, BiologicalSex.Male);
            s.Reception.Store.Patients.Add(other);
            s.Reception.Store.BiometricReferences.Add(PatientBiometricReference.Register(
                other.Id, BiometricModality.Fingerprint, "R307", FingerprintReferences.SubjectReference(s.Reader.Id, Slot)));
            var enrollment = await OpenAsync(s);

            await Assert.ThrowsAsync<ResourceConflictException>(() => SubmitAsync(s, enrollment.Id, FingerprintEnrollmentOutcome.Enrolled, Slot));

            Assert.Equal(FingerprintEnrollmentStatus.Pending, Stored(s, enrollment.Id).Status);
            await SubmitAsync(s, enrollment.Id, FingerprintEnrollmentOutcome.Enrolled, Slot + 1);
            Assert.Equal(FingerprintEnrollmentStatus.Enrolled, Stored(s, enrollment.Id).Status);
        }

        // One active reference per patient and reader; another reader is another reference (a reference belongs to its reader).
        [Fact]
        public async Task APatientHasAtMostOneActiveFingerprintPerReader()
        {
            var s = Start();
            var first = await OpenAsync(s);
            await SubmitAsync(s, first.Id, FingerprintEnrollmentOutcome.Enrolled, Slot);

            await Assert.ThrowsAsync<ResourceConflictException>(() => OpenAsync(s));

            var second = s.Reception.Readers.Add("ESP32-HOSP-02", DeviceCredentialStatus.Active, DeviceScopes.FingerprintEnroll, DeviceScopes.FingerprintIdentify);
            var other = await OpenAsync(s, readerId: second.Id);
            await SubmitAsync(s, other.Id, FingerprintEnrollmentOutcome.Enrolled, Slot, second);
            Assert.Equal(2, s.Reception.Store.BiometricReferences.Count(r => r.Status == BiometricReferenceStatus.Active));
        }

        // The rule is checked again when the result arrives (a reference created meanwhile for the same patient and reader).
        [Fact]
        public async Task TheReaderRuleIsCheckedAgainWhenTheResultArrives()
        {
            var s = Start();
            var enrollment = await OpenAsync(s);
            s.Reception.Store.BiometricReferences.Add(PatientBiometricReference.Register(
                s.Patient.Id, BiometricModality.Fingerprint, "R307", FingerprintReferences.SubjectReference(s.Reader.Id, 99)));

            await Assert.ThrowsAsync<ResourceConflictException>(() => SubmitAsync(s, enrollment.Id, FingerprintEnrollmentOutcome.Enrolled, Slot));

            Assert.Single(s.Reception.Store.BiometricReferences);
        }

        // A revoked reference keeps its history and its slot can be enrolled again.
        [Fact]
        public async Task ARevokedReferenceFreesItsSlot()
        {
            var s = Start();
            var first = await OpenAsync(s);
            await SubmitAsync(s, first.Id, FingerprintEnrollmentOutcome.Enrolled, Slot);
            s.Reception.Store.BiometricReferences.Single().Revoke(DateTimeOffset.UtcNow);

            var again = await OpenAsync(s);
            await SubmitAsync(s, again.Id, FingerprintEnrollmentOutcome.Enrolled, Slot);

            Assert.Equal([BiometricReferenceStatus.Active, BiometricReferenceStatus.Revoked],
                s.Reception.Store.BiometricReferences.Select(r => r.Status).Order());
            Assert.Equal(2, (await s.Reception.FingerprintEnrollments.ListPatientFingerprintsAsync(s.Patient.Id, None)).Count);
        }

        [Fact]
        public async Task OneEnrollmentIsPendingPerPatientAndPerReader()
        {
            var s = Start();
            await OpenAsync(s);
            var otherReader = s.Reception.Readers.Add("ESP32-HOSP-02", DeviceCredentialStatus.Active, DeviceScopes.FingerprintEnroll, DeviceScopes.FingerprintIdentify);
            var otherPatient = Patient.Register("Luis", "Gómez", null, BiologicalSex.Male);
            s.Reception.Store.Patients.Add(otherPatient);

            await Assert.ThrowsAsync<ResourceConflictException>(() => OpenAsync(s, readerId: otherReader.Id));
            await Assert.ThrowsAsync<ResourceConflictException>(() => OpenAsync(s, patientId: otherPatient.Id));
            await Assert.ThrowsAsync<ResourceConflictException>(() => OpenAsync(s, key: Guid.NewGuid()));
            Assert.Single(s.Reception.Store.Enrollments);
        }

        // At 120 s the enrollment is over: the late result is rejected (the reader deletes its slot) and both are free.
        [Fact]
        public async Task AnExpiredEnrollmentRejectsTheResultAndFreesThePatientAndTheReader()
        {
            var s = Start();
            var enrollment = await OpenAsync(s);
            s.Reception.Time.Now = enrollment.ExpiresAtUtc;

            Assert.Null(await s.Reception.EnrollmentReaderSide.GetPendingAsync(s.Reader.Id, None));
            await Assert.ThrowsAsync<ResourceConflictException>(() => SubmitAsync(s, enrollment.Id, FingerprintEnrollmentOutcome.Enrolled, Slot));

            Assert.Equal(FingerprintEnrollmentStatus.Expired, (await s.Reception.FingerprintEnrollments.GetAsync(s.Patient.Id, enrollment.Id, None)).Status);
            Assert.Empty(s.Reception.Store.BiometricReferences);
            Assert.Equal(FingerprintEnrollmentStatus.Pending, (await OpenAsync(s)).Status);
        }

        [Fact]
        public async Task CancellingEndsAPendingEnrollmentOnceAndNeverACompletedOne()
        {
            var s = Start();
            var pending = await OpenAsync(s);

            var cancelled = await s.Reception.FingerprintEnrollments.CancelAsync(s.Patient.Id, pending.Id, None);
            var again = await s.Reception.FingerprintEnrollments.CancelAsync(s.Patient.Id, pending.Id, None);

            Assert.Equal((FingerprintEnrollmentStatus.Cancelled, FingerprintEnrollmentStatus.Cancelled), (cancelled.Status, again.Status));
            Assert.Single(s.Reception.Store.Audit, a => a.Action == AuditActionType.Update);
            await Assert.ThrowsAsync<ResourceConflictException>(() => SubmitAsync(s, pending.Id, FingerprintEnrollmentOutcome.Enrolled, Slot));

            var failed = await OpenAsync(s);
            await SubmitAsync(s, failed.Id, FingerprintEnrollmentOutcome.SensorError);
            Assert.Equal(FingerprintEnrollmentStatus.Failed, (await s.Reception.FingerprintEnrollments.CancelAsync(s.Patient.Id, failed.Id, None)).Status);

            var enrolled = await OpenAsync(s);
            await SubmitAsync(s, enrolled.Id, FingerprintEnrollmentOutcome.Enrolled, Slot);
            await Assert.ThrowsAsync<ResourceConflictException>(() => s.Reception.FingerprintEnrollments.CancelAsync(s.Patient.Id, enrolled.Id, None));
            Assert.Equal(BiometricReferenceStatus.Active, s.Reception.Store.BiometricReferences.Single().Status);
        }

        // The reader only sees its own enrollments; the result is validated before anything is written.
        [Fact]
        public async Task TheReaderOnlyAnswersItsOwnEnrollmentAndTheResultIsStrict()
        {
            var s = Start();
            var enrollment = await OpenAsync(s);
            var other = s.Reception.Readers.Add("ESP32-HOSP-02", DeviceCredentialStatus.Active, DeviceScopes.FingerprintEnroll, DeviceScopes.FingerprintIdentify);

            Assert.Null(await s.Reception.EnrollmentReaderSide.GetPendingAsync(other.Id, None));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => SubmitAsync(s, enrollment.Id, FingerprintEnrollmentOutcome.Enrolled, Slot, other));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => SubmitAsync(s, Guid.NewGuid(), FingerprintEnrollmentOutcome.Enrolled, Slot));
            foreach (var (outcome, slot) in new (FingerprintEnrollmentOutcome, int?)[]
            {
                (FingerprintEnrollmentOutcome.Enrolled, null), (FingerprintEnrollmentOutcome.Enrolled, -1),
                (FingerprintEnrollmentOutcome.Failed, 1), ((FingerprintEnrollmentOutcome)0, null)
            })
            {
                await Assert.ThrowsAsync<RequestValidationException>(() => SubmitAsync(s, enrollment.Id, outcome, slot));
            }

            Assert.Equal(FingerprintEnrollmentStatus.Pending, Stored(s, enrollment.Id).Status);
        }

        // Biometrics.Manage alone is not enough: active clinical staff who can see the patient. No role bypass.
        [Theory]
        [InlineData(SecurityRoles.Doctor)]
        [InlineData(SecurityRoles.Nurse)]
        [InlineData(SecurityRoles.Paramedic)]
        public async Task OnlyStaffWithBiometricsManageCanEnroll(string role)
        {
            var s = Start();
            var other = new ClinicalHarness(FakeCurrentUser.InRole(role), s.Reception.Store);
            other.Staff.Add(other.User.UserId!.Value, true, Guid.NewGuid());

            await Assert.ThrowsAsync<AccessDeniedException>(() =>
                other.FingerprintEnrollments.OpenAsync(s.Patient.Id, new OpenFingerprintEnrollmentRequest(Guid.NewGuid(), s.Reader.Id), None));
            await Assert.ThrowsAsync<AccessDeniedException>(() => other.FingerprintEnrollments.ListPatientFingerprintsAsync(s.Patient.Id, None));
        }

        [Fact]
        public async Task AnAdministratorWithoutClinicalStaffCannotEnrollAndAnUnknownPatientIsNotFound()
        {
            var s = Start();
            var admin = new ClinicalHarness(FakeCurrentUser.InRole(SecurityRoles.Administrator), s.Reception.Store);

            await Assert.ThrowsAsync<AccessDeniedException>(() =>
                admin.FingerprintEnrollments.OpenAsync(s.Patient.Id, new OpenFingerprintEnrollmentRequest(Guid.NewGuid(), s.Reader.Id), None));
            admin.Staff.Add(admin.User.UserId!.Value, true, Guid.NewGuid());
            admin.Readers.Devices.AddRange(s.Reception.Readers.Devices);
            Assert.Equal(FingerprintEnrollmentStatus.Pending, (await admin.FingerprintEnrollments.OpenAsync(
                s.Patient.Id, new OpenFingerprintEnrollmentRequest(Guid.NewGuid(), s.Reader.Id), None)).Enrollment.Status);
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => OpenAsync(s, patientId: Guid.NewGuid()));
        }

        // D6: the reference of a Matched identification is revoked while the paramedic confirms: the confirmation loses
        // (409), the emergency stays NN and the session stays Matched.
        [Fact]
        public async Task AConfirmationLosesAgainstARevocationOfItsReference()
        {
            var paramedic = ClinicalHarness.ForRole(SecurityRoles.Paramedic);
            var opened = await paramedic.Emergencies.OpenAsync(
                new OpenPrehospitalEmergencyRequest(Guid.NewGuid(), ClinicalHarness.Start.AddMinutes(-5), "Inconsciente"), None);
            var reader = paramedic.Readers.Add("ESP32-AMB-01");
            var patient = Patient.Register("Ana", "Pérez", null, BiologicalSex.Female);
            paramedic.Store.Patients.Add(patient);
            var reference = PatientBiometricReference.Register(
                patient.Id, BiometricModality.Fingerprint, "R307", FingerprintReferences.SubjectReference(reader.Id, Slot));
            paramedic.Store.BiometricReferences.Add(reference);
            var session = await paramedic.Fingerprints.OpenSessionAsync(opened.Emergency.Id, new OpenFingerprintSessionRequest(Guid.NewGuid(), reader.Id), None);
            await paramedic.ReaderSide.SubmitResultAsync(reader.Id, reader.DeviceCode, session.Session.Id, new SubmitFingerprintResultRequest(FingerprintCaptureOutcome.Match, Slot), None);
            // The revocation commits after the confirmation read the reference and before its own save.
            paramedic.Store.BeforeSave = () => reference.Revoke(DateTimeOffset.UtcNow);

            await Assert.ThrowsAsync<ResourceConflictException>(() =>
                paramedic.Fingerprints.ConfirmAsync(opened.Emergency.Id, session.Session.Id, new ConfirmFingerprintSessionRequest(Guid.NewGuid()), None));

            // The whole save was discarded (the in-memory fake cannot undo the tracked encounter; PostgreSQL does, see
            // FingerprintEnrollmentApiTests): no session change, no receipt, no Identify entry.
            Assert.Equal(FingerprintSessionStatus.Matched, paramedic.Store.Sessions.Single().Status);
            Assert.DoesNotContain(paramedic.Store.Receipts, r => r.Key.Operation == EmergencyFingerprintService.ConfirmOperation);
            Assert.DoesNotContain(paramedic.Store.Audit, a => a.Action == AuditActionType.Identify);
        }
    }
}
