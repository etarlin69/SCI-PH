using SCI_PH.Domain.Common;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Entities.Documents;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Enums.Patients;
using SCI_PH.Domain.Exceptions;

namespace SCI_PH.Domain.Entities.Patients
{
    // Clinical record of a person inside SCI-PH. It is not an account and not an official national registry.
    public sealed class Patient
    {
        private readonly List<PatientIdentifier> _identifiers = [];
        private readonly List<PatientMedicalHistory> _medicalHistories = [];
        private readonly List<ClinicalEncounter> _encounters = [];
        private readonly List<ClinicalDocument> _documents = [];
        private readonly List<PatientBiometricReference> _biometricReferences = [];

        private Patient() { }

        public Guid Id { get; private set; }
        // Optional account managed by IdentityDbContext; intentionally no cross-context foreign key.
        public Guid? IdentityUserId { get; private set; }
        public PatientIdentityStatus IdentityStatus { get; private set; }
        public string GivenNames { get; private set; } = string.Empty;
        public string FamilyNames { get; private set; } = string.Empty;
        public DateOnly? DateOfBirth { get; private set; }
        public BiologicalSex Sex { get; private set; }
        public DateTimeOffset CreatedAtUtc { get; private set; }
        public DateTimeOffset? UpdatedAtUtc { get; private set; }

        public IReadOnlyCollection<PatientIdentifier> Identifiers => _identifiers.AsReadOnly();
        public IReadOnlyCollection<PatientMedicalHistory> MedicalHistories => _medicalHistories.AsReadOnly();
        public IReadOnlyCollection<ClinicalEncounter> Encounters => _encounters.AsReadOnly();
        public IReadOnlyCollection<ClinicalDocument> Documents => _documents.AsReadOnly();
        public IReadOnlyCollection<PatientBiometricReference> BiometricReferences => _biometricReferences.AsReadOnly();

        public static Patient Register(string givenNames, string familyNames, DateOnly? dateOfBirth, BiologicalSex sex)
        {
            var patient = new Patient
            {
                Id = Guid.NewGuid(),
                IdentityStatus = PatientIdentityStatus.Unverified,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            patient.SetDemographics(givenNames, familyNames, dateOfBirth, sex);
            return patient;
        }

        public void UpdateDemographics(string givenNames, string familyNames, DateOnly? dateOfBirth, BiologicalSex sex)
        {
            SetDemographics(givenNames, familyNames, dateOfBirth, sex);
            Touch();
        }

        public void LinkIdentityUser(Guid identityUserId)
        {
            Guard.Id(identityUserId, nameof(identityUserId));
            if (IdentityUserId.HasValue && IdentityUserId != identityUserId)
            {
                throw new DomainException("The patient is already linked to another account.");
            }

            IdentityUserId = identityUserId;
            Touch();
        }

        public void UnlinkIdentityUser()
        {
            IdentityUserId = null;
            Touch();
        }

        public void MarkIdentityVerified()
        {
            IdentityStatus = PatientIdentityStatus.Verified;
            Touch();
        }

        public PatientIdentifier AddIdentifier(
            PatientIdentifierType type,
            string value,
            PatientIdentifierSource source,
            string? issuer = null,
            bool isPrimary = false,
            DateTimeOffset? validUntilUtc = null)
        {
            var identifier = new PatientIdentifier(Id, type, value, source, issuer, isPrimary, validUntilUtc);
            if (_identifiers.Any(existing => existing.IsActive && existing.HasSameValueAs(identifier)))
            {
                throw new InvalidPatientIdentifierException($"The patient already has an active {type} identifier with this value.");
            }

            if (isPrimary)
            {
                DemotePrimary(type);
            }

            _identifiers.Add(identifier);
            Touch();
            return identifier;
        }

        public void MakePrimaryIdentifier(Guid identifierId)
        {
            var identifier = FindIdentifier(identifierId);
            DemotePrimary(identifier.Type);
            identifier.Promote();
            Touch();
        }

        public void VerifyIdentifier(Guid identifierId, DateTimeOffset verifiedAtUtc)
        {
            FindIdentifier(identifierId).Verify(verifiedAtUtc);
            Touch();
        }

        public void RevokeIdentifier(Guid identifierId, DateTimeOffset revokedAtUtc)
        {
            FindIdentifier(identifierId).Revoke(revokedAtUtc);
            Touch();
        }

        private void SetDemographics(string givenNames, string familyNames, DateOnly? dateOfBirth, BiologicalSex sex)
        {
            if (dateOfBirth > DateOnly.FromDateTime(DateTime.UtcNow))
            {
                throw new ArgumentOutOfRangeException(nameof(dateOfBirth), dateOfBirth, "Date of birth cannot be in the future.");
            }

            GivenNames = Guard.Text(givenNames, FieldLengths.PersonName, nameof(givenNames));
            FamilyNames = Guard.Text(familyNames, FieldLengths.PersonName, nameof(familyNames));
            DateOfBirth = dateOfBirth;
            Sex = Guard.Defined(sex, nameof(sex));
        }

        private void DemotePrimary(PatientIdentifierType type)
        {
            foreach (var current in _identifiers.Where(x => x.IsActive && x.IsPrimary && x.Type == type))
            {
                current.Demote();
            }
        }

        private PatientIdentifier FindIdentifier(Guid identifierId) =>
            _identifiers.SingleOrDefault(x => x.Id == identifierId)
                ?? throw new InvalidPatientIdentifierException($"Identifier {identifierId} does not belong to this patient.");

        private void Touch() => UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}
