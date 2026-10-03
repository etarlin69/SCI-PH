using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Entities.Organizations;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Patients;
using static SCI_PH.Persistence.Configurations.Sql;

namespace SCI_PH.Persistence.Configurations.Clinical.Patients
{
    internal sealed class IdentificationAttemptConfiguration : IEntityTypeConfiguration<IdentificationAttempt>
    {
        public void Configure(EntityTypeBuilder<IdentificationAttempt> builder)
        {
            const string outcome = nameof(IdentificationAttempt.Outcome);
            const string method = nameof(IdentificationAttempt.Method);
            const string patientId = nameof(IdentificationAttempt.PatientId);
            const string failureReason = nameof(IdentificationAttempt.FailureReason);
            const string identifierId = nameof(IdentificationAttempt.PatientIdentifierId);
            const string biometricReferenceId = nameof(IdentificationAttempt.BiometricReferenceId);

            builder.HasKey(x => x.Id);
            builder.Property(x => x.DeviceReference).HasMaxLength(FieldLengths.ExternalReference);
            builder.Property(x => x.Provider).HasMaxLength(FieldLengths.ProviderName);
            builder.Property(x => x.ProviderRequestReference).HasMaxLength(FieldLengths.ExternalReference);

            builder.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId);
            builder.HasOne<ClinicalEncounter>().WithMany().HasForeignKey(x => x.ClinicalEncounterId);
            builder.HasOne<ClinicalStaff>().WithMany().HasForeignKey(x => x.PerformedByStaffId);
            // Composite keys: the matched identifier or biometric reference must belong to the matched patient.
            builder.HasOne<PatientIdentifier>().WithMany()
                .HasForeignKey(x => new { x.PatientIdentifierId, x.PatientId })
                .HasPrincipalKey(x => new { x.Id, x.PatientId });
            builder.HasOne<PatientBiometricReference>().WithMany()
                .HasForeignKey(x => new { x.BiometricReferenceId, x.PatientId })
                .HasPrincipalKey(x => new { x.Id, x.PatientId });

            builder.HasIndex(x => new { x.PatientId, x.OccurredAtUtc });
            builder.HasIndex(x => new { x.Method, x.OccurredAtUtc });
            builder.HasIndex(x => new { x.ClinicalEncounterId, x.OccurredAtUtc });

            builder.ToTable(table =>
            {
                table.HasCheckConstraint("CK_IdentificationAttempts_Outcome", Any(
                    All(Is(outcome, IdentificationOutcome.Success), IsNotNull(patientId), IsNull(failureReason)),
                    All(Is(outcome, IdentificationOutcome.Failure), IsNull(patientId), IsNotNull(failureReason))));
                table.HasCheckConstraint("CK_IdentificationAttempts_Evidence", All(
                    Implies(IsNotNull(identifierId), In(method, IdentificationAttempt.IdentifierMethods)),
                    Implies(IsNotNull(biometricReferenceId), In(method, IdentificationAttempt.BiometricMethods))));
                table.HasCheckConstraint("CK_IdentificationAttempts_Actor", Any(
                    IsNotNull(nameof(IdentificationAttempt.ActorIdentityUserId)),
                    IsNotNull(nameof(IdentificationAttempt.PerformedByStaffId)),
                    IsNotNull(nameof(IdentificationAttempt.DeviceReference))));
            });
        }
    }
}
