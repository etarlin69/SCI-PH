using SCI_PH.Domain.Common;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Enums.Patients;
using SCI_PH.Domain.Exceptions;

namespace SCI_PH.Domain.Entities.Patients
{
    // Enrollment reference held by the external biometric provider.
    // Raw images, fingerprints and templates are never stored in SCI-PH.
    public sealed class PatientBiometricReference
    {
        private PatientBiometricReference() { }

        public Guid Id { get; private set; }
        public Guid PatientId { get; private set; }
        public Patient Patient { get; private set; } = null!;
        public BiometricModality Modality { get; private set; }
        public string Provider { get; private set; } = string.Empty;
        public string SubjectReference { get; private set; } = string.Empty;
        public string? AlgorithmVersion { get; private set; }
        public BiometricReferenceStatus Status { get; private set; }
        public Guid? RegisteredByStaffId { get; private set; }
        public DateTimeOffset CreatedAtUtc { get; private set; }
        public DateTimeOffset? RevokedAtUtc { get; private set; }

        public static PatientBiometricReference Register(
            Guid patientId,
            BiometricModality modality,
            string provider,
            string subjectReference,
            Guid? registeredByStaffId = null,
            string? algorithmVersion = null) =>
            new()
            {
                Id = Guid.NewGuid(),
                PatientId = Guard.Id(patientId, nameof(patientId)),
                Modality = Guard.Defined(modality, nameof(modality)),
                Provider = Guard.Text(provider, FieldLengths.ProviderName, nameof(provider)),
                SubjectReference = Guard.Text(subjectReference, FieldLengths.ExternalReference, nameof(subjectReference)),
                AlgorithmVersion = Guard.OptionalText(algorithmVersion, FieldLengths.Code, nameof(algorithmVersion)),
                Status = BiometricReferenceStatus.Active,
                RegisteredByStaffId = Guard.OptionalId(registeredByStaffId, nameof(registeredByStaffId)),
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

        public void Revoke(DateTimeOffset revokedAtUtc)
        {
            if (Status == BiometricReferenceStatus.Revoked)
            {
                throw new DomainException("The biometric reference is already revoked.");
            }

            RevokedAtUtc = Guard.NotBefore(revokedAtUtc, CreatedAtUtc, nameof(revokedAtUtc));
            Status = BiometricReferenceStatus.Revoked;
        }
    }
}
