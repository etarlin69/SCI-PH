using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SCI_PH.Application.Abstractions.Biometrics;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Persistence.Clinical.Biometrics;
using static SCI_PH.Persistence.Configurations.Sql;

namespace SCI_PH.Persistence.Configurations.Clinical.Biometrics
{
    // Applied explicitly by ClinicalDbContext (it is not a Domain entity). One job per reference (unique foreign key,
    // restricted delete). Status and Attempts are concurrency tokens: of two workers on the same job only one result applies.
    internal sealed class FaceTemplateDeletionJobConfiguration : IEntityTypeConfiguration<FaceTemplateDeletionJobEntry>
    {
        public const string PerReferenceIndexName = "UX_FaceTemplateDeletionJobs_Reference";
        public const string PendingIndexName = "IX_FaceTemplateDeletionJobs_Pending";

        public void Configure(EntityTypeBuilder<FaceTemplateDeletionJobEntry> builder)
        {
            const string status = nameof(FaceTemplateDeletionJobEntry.Status);
            const string outcome = nameof(FaceTemplateDeletionJobEntry.Outcome);
            const string attempts = nameof(FaceTemplateDeletionJobEntry.Attempts);
            const string lastAttemptAt = nameof(FaceTemplateDeletionJobEntry.LastAttemptAtUtc);
            const string createdAt = nameof(FaceTemplateDeletionJobEntry.CreatedAtUtc);
            const string completedAt = nameof(FaceTemplateDeletionJobEntry.CompletedAtUtc);

            builder.ToTable("FaceTemplateDeletionJobs");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Status).IsConcurrencyToken();
            builder.Property(x => x.Attempts).IsConcurrencyToken();

            builder.HasOne<PatientBiometricReference>().WithMany().HasForeignKey(x => x.BiometricReferenceId).OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(x => x.BiometricReferenceId).IsUnique().HasDatabaseName(PerReferenceIndexName);
            builder.HasIndex(x => x.CreatedAtUtc)
                .HasDatabaseName(PendingIndexName)
                .HasFilter(Is(status, FaceTemplateDeletionStatus.Pending));

            builder.ToTable(table =>
            {
                table.HasCheckConstraint("CK_FaceTemplateDeletionJobs_Dates", All(
                    NotBefore(lastAttemptAt, createdAt),
                    NotBefore(completedAt, createdAt)));
                table.HasCheckConstraint("CK_FaceTemplateDeletionJobs_Attempts", $"{Column(attempts)} >= 0");
                // Pending has no outcome; Completed has its outcome and its time.
                table.HasCheckConstraint("CK_FaceTemplateDeletionJobs_Lifecycle", Any(
                    All(Is(status, FaceTemplateDeletionStatus.Pending), IsNull(outcome), IsNull(completedAt)),
                    All(Is(status, FaceTemplateDeletionStatus.Completed), IsNotNull(outcome), IsNotNull(completedAt))));
            });
        }
    }
}
