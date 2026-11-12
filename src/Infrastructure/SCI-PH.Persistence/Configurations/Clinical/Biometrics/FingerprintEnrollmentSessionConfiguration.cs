using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SCI_PH.Application.Abstractions.Biometrics;
using SCI_PH.Application.Common.Idempotency;
using SCI_PH.Domain.Entities.Organizations;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Persistence.Clinical.Biometrics;
using static SCI_PH.Persistence.Configurations.Sql;

namespace SCI_PH.Persistence.Configurations.Clinical.Biometrics
{
    // Applied explicitly by ClinicalDbContext (it is not a Domain entity). Status is a concurrency token, so of a result, a
    // cancellation and an expiry only one commits. Only Pending is active: the unique filtered indexes keep one Pending
    // enrollment per patient and per reader, also under concurrent requests. DeviceCredentialId has no foreign key.
    internal sealed class FingerprintEnrollmentSessionConfiguration : IEntityTypeConfiguration<FingerprintEnrollmentSessionEntry>
    {
        public const string PendingPerPatientIndexName = "UX_FingerprintEnrollmentSessions_PendingPerPatient";
        public const string PendingPerReaderIndexName = "UX_FingerprintEnrollmentSessions_PendingPerReader";

        public void Configure(EntityTypeBuilder<FingerprintEnrollmentSessionEntry> builder)
        {
            const string status = nameof(FingerprintEnrollmentSessionEntry.Status);
            const string failureReason = nameof(FingerprintEnrollmentSessionEntry.FailureReason);
            const string resultHash = nameof(FingerprintEnrollmentSessionEntry.ResultHash);
            const string reference = nameof(FingerprintEnrollmentSessionEntry.BiometricReferenceId);
            const string createdAt = nameof(FingerprintEnrollmentSessionEntry.CreatedAtUtc);
            const string expiresAt = nameof(FingerprintEnrollmentSessionEntry.ExpiresAtUtc);
            const string resolvedAt = nameof(FingerprintEnrollmentSessionEntry.ResolvedAtUtc);

            builder.ToTable("FingerprintEnrollmentSessions");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Status).IsConcurrencyToken();
            builder.Property(x => x.ResultHash).HasMaxLength(IdempotencyRequestHash.Length).IsFixedLength();

            builder.HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId);
            builder.HasOne<ClinicalStaff>().WithMany().HasForeignKey(x => x.RequestedByStaffId);
            builder.HasOne<PatientBiometricReference>().WithMany().HasForeignKey(x => x.BiometricReferenceId);

            builder.HasIndex(x => x.PatientId)
                .IsUnique()
                .HasDatabaseName(PendingPerPatientIndexName)
                .HasFilter(Is(status, FingerprintEnrollmentStatus.Pending));
            builder.HasIndex(x => x.DeviceCredentialId)
                .IsUnique()
                .HasDatabaseName(PendingPerReaderIndexName)
                .HasFilter(Is(status, FingerprintEnrollmentStatus.Pending));

            builder.ToTable(table =>
            {
                table.HasCheckConstraint("CK_FingerprintEnrollmentSessions_ResultHash",
                    Any(IsNull(resultHash), $"length({Column(resultHash)}) = {IdempotencyRequestHash.Length}"));
                table.HasCheckConstraint("CK_FingerprintEnrollmentSessions_Dates", All(
                    After(expiresAt, createdAt),
                    NotBefore(resolvedAt, createdAt)));
                // A reader's result is a hash and a time; it leaves either the created reference or a failure reason.
                // Expired and Cancelled never had an accepted result.
                table.HasCheckConstraint("CK_FingerprintEnrollmentSessions_Lifecycle", All(
                    BothOrNeither(resolvedAt, resultHash),
                    Any(
                        All(In(status, [FingerprintEnrollmentStatus.Pending, FingerprintEnrollmentStatus.Expired, FingerprintEnrollmentStatus.Cancelled]),
                            IsNull(resolvedAt), IsNull(reference), IsNull(failureReason)),
                        All(Is(status, FingerprintEnrollmentStatus.Enrolled), IsNotNull(resolvedAt), IsNotNull(reference), IsNull(failureReason)),
                        All(Is(status, FingerprintEnrollmentStatus.Failed), IsNotNull(resolvedAt), IsNull(reference), IsNotNull(failureReason)))));
            });
        }
    }
}
