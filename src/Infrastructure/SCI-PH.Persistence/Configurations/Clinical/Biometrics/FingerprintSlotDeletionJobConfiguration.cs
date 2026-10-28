using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SCI_PH.Application.Abstractions.Biometrics;
using SCI_PH.Application.Common.Idempotency;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Persistence.Clinical.Biometrics;
using static SCI_PH.Persistence.Configurations.Sql;

namespace SCI_PH.Persistence.Configurations.Clinical.Biometrics
{
    // Applied explicitly by ClinicalDbContext (it is not a Domain entity). One job per reference (unique foreign key,
    // restricted delete: the biometric history is never removed through it). Status and LastAttemptId are concurrency
    // tokens, so of two reports of the reader only one applies. DeviceCredentialId has no foreign key.
    internal sealed class FingerprintSlotDeletionJobConfiguration : IEntityTypeConfiguration<FingerprintSlotDeletionJobEntry>
    {
        public const string PerReferenceIndexName = "UX_FingerprintSlotDeletionJobs_Reference";
        public const string PendingPerReaderIndexName = "IX_FingerprintSlotDeletionJobs_PendingPerReader";

        public void Configure(EntityTypeBuilder<FingerprintSlotDeletionJobEntry> builder)
        {
            const string status = nameof(FingerprintSlotDeletionJobEntry.Status);
            const string outcome = nameof(FingerprintSlotDeletionJobEntry.Outcome);
            const string attempts = nameof(FingerprintSlotDeletionJobEntry.Attempts);
            const string lastAttemptId = nameof(FingerprintSlotDeletionJobEntry.LastAttemptId);
            const string resultHash = nameof(FingerprintSlotDeletionJobEntry.ResultHash);
            const string lastAttemptAt = nameof(FingerprintSlotDeletionJobEntry.LastAttemptAtUtc);
            const string createdAt = nameof(FingerprintSlotDeletionJobEntry.CreatedAtUtc);
            const string completedAt = nameof(FingerprintSlotDeletionJobEntry.CompletedAtUtc);

            builder.ToTable("FingerprintSlotDeletionJobs");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Status).IsConcurrencyToken();
            builder.Property(x => x.LastAttemptId).IsConcurrencyToken();
            builder.Property(x => x.ResultHash).HasMaxLength(IdempotencyRequestHash.Length).IsFixedLength();

            builder.HasOne<PatientBiometricReference>().WithMany().HasForeignKey(x => x.BiometricReferenceId).OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(x => x.BiometricReferenceId).IsUnique().HasDatabaseName(PerReferenceIndexName);
            builder.HasIndex(x => new { x.DeviceCredentialId, x.CreatedAtUtc })
                .HasDatabaseName(PendingPerReaderIndexName)
                .HasFilter(Is(status, FingerprintSlotDeletionStatus.Pending));

            builder.ToTable(table =>
            {
                table.HasCheckConstraint("CK_FingerprintSlotDeletionJobs_ResultHash",
                    Any(IsNull(resultHash), $"length({Column(resultHash)}) = {IdempotencyRequestHash.Length}"));
                table.HasCheckConstraint("CK_FingerprintSlotDeletionJobs_Dates", All(
                    NotBefore(lastAttemptAt, createdAt),
                    NotBefore(completedAt, createdAt)));
                // An attempt is an id, a hash and a time together; none before the first report.
                table.HasCheckConstraint("CK_FingerprintSlotDeletionJobs_Attempts", All(
                    $"{Column(attempts)} >= 0",
                    BothOrNeither(lastAttemptId, resultHash),
                    BothOrNeither(lastAttemptId, lastAttemptAt),
                    Any(All($"{Column(attempts)} = 0", IsNull(lastAttemptId)), All($"{Column(attempts)} > 0", IsNotNull(lastAttemptId)))));
                // Pending has no outcome; Completed has its outcome, its time and the attempt that completed it.
                table.HasCheckConstraint("CK_FingerprintSlotDeletionJobs_Lifecycle", Any(
                    All(Is(status, FingerprintSlotDeletionStatus.Pending), IsNull(outcome), IsNull(completedAt)),
                    All(Is(status, FingerprintSlotDeletionStatus.Completed), IsNotNull(outcome), IsNotNull(completedAt), IsNotNull(lastAttemptId))));
            });
        }
    }
}
