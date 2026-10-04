using SCI_PH.Domain.Common;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Enums.Patients;
using SCI_PH.Domain.Exceptions;

namespace SCI_PH.Domain.Entities.Patients
{
    // One attempt to identify a person by QR, document, biometrics or manual review.
    // It records the event only; captures and templates are never stored here.
    // A successful attempt does not link an encounter by itself (see ClinicalEncounter.IdentifyPatient).
    public sealed class IdentificationAttempt
    {
        public static IReadOnlyCollection<IdentificationMethod> BiometricMethods { get; } =
            [IdentificationMethod.Face, IdentificationMethod.Fingerprint];

        public static IReadOnlyCollection<IdentificationMethod> IdentifierMethods { get; } =
        [
            IdentificationMethod.QrCode,
            IdentificationMethod.NationalId,
            IdentificationMethod.Passport,
            IdentificationMethod.InternalCode,
            IdentificationMethod.Phone
        ];

        private IdentificationAttempt() { }

        public Guid Id { get; private set; }
        public IdentificationMethod Method { get; private set; }
        public IdentificationOutcome Outcome { get; private set; }
        public IdentificationFailureReason? FailureReason { get; private set; }
        public Guid? PatientId { get; private set; }
        public Patient? Patient { get; private set; }
        public Guid? PatientIdentifierId { get; private set; }
        public Guid? BiometricReferenceId { get; private set; }
        public Guid? ClinicalEncounterId { get; private set; }
        public string? DeviceReference { get; private set; }
        public Guid? ActorIdentityUserId { get; private set; }
        public Guid? PerformedByStaffId { get; private set; }
        public string? Provider { get; private set; }
        public string? ProviderRequestReference { get; private set; }
        public DateTimeOffset OccurredAtUtc { get; private set; }

        public static IdentificationAttempt Succeeded(
            IdentificationMethod method,
            Guid patientId,
            DateTimeOffset occurredAtUtc,
            IdentificationAttemptContext context,
            Guid? patientIdentifierId = null,
            Guid? biometricReferenceId = null)
        {
            var attempt = Create(method, IdentificationOutcome.Success, occurredAtUtc, context);
            attempt.PatientId = Guard.Id(patientId, nameof(patientId));
            attempt.PatientIdentifierId = attempt.EvidenceFor(IdentifierMethods, patientIdentifierId, nameof(patientIdentifierId));
            attempt.BiometricReferenceId = attempt.EvidenceFor(BiometricMethods, biometricReferenceId, nameof(biometricReferenceId));
            return attempt;
        }

        public static IdentificationAttempt Failed(
            IdentificationMethod method,
            IdentificationFailureReason reason,
            DateTimeOffset occurredAtUtc,
            IdentificationAttemptContext context)
        {
            var attempt = Create(method, IdentificationOutcome.Failure, occurredAtUtc, context);
            attempt.FailureReason = Guard.Defined(reason, nameof(reason));
            return attempt;
        }

        private static IdentificationAttempt Create(
            IdentificationMethod method,
            IdentificationOutcome outcome,
            DateTimeOffset occurredAtUtc,
            IdentificationAttemptContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (context.ActorIdentityUserId is null && context.PerformedByStaffId is null && string.IsNullOrWhiteSpace(context.DeviceReference))
            {
                throw new DomainException("An identification attempt must record a user, staff member or device.");
            }

            return new IdentificationAttempt
            {
                Id = Guid.NewGuid(),
                Method = Guard.Defined(method, nameof(method)),
                Outcome = outcome,
                OccurredAtUtc = Guard.Utc(occurredAtUtc),
                ClinicalEncounterId = Guard.OptionalId(context.ClinicalEncounterId, nameof(context.ClinicalEncounterId)),
                DeviceReference = Guard.OptionalText(context.DeviceReference, FieldLengths.ExternalReference, nameof(context.DeviceReference)),
                ActorIdentityUserId = Guard.OptionalId(context.ActorIdentityUserId, nameof(context.ActorIdentityUserId)),
                PerformedByStaffId = Guard.OptionalId(context.PerformedByStaffId, nameof(context.PerformedByStaffId)),
                Provider = Guard.OptionalText(context.Provider, FieldLengths.ProviderName, nameof(context.Provider)),
                ProviderRequestReference = Guard.OptionalText(
                    context.ProviderRequestReference, FieldLengths.ExternalReference, nameof(context.ProviderRequestReference))
            };
        }

        private Guid? EvidenceFor(IReadOnlyCollection<IdentificationMethod> allowedMethods, Guid? evidenceId, string paramName)
        {
            if (evidenceId is not null && !allowedMethods.Contains(Method))
            {
                throw new DomainException($"{paramName} does not apply to {Method} identification.");
            }

            return Guard.OptionalId(evidenceId, paramName);
        }
    }

    // Who or what performed the attempt and where. At least one actor is required.
    public sealed record IdentificationAttemptContext(
        Guid? ActorIdentityUserId = null,
        Guid? PerformedByStaffId = null,
        string? DeviceReference = null,
        Guid? ClinicalEncounterId = null,
        string? Provider = null,
        string? ProviderRequestReference = null);
}
