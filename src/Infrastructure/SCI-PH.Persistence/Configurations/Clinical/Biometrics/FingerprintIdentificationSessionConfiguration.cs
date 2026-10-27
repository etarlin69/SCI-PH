using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SCI_PH.Application.Abstractions.Biometrics;
using SCI_PH.Application.Common.Idempotency;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Entities.Organizations;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Persistence.Clinical.Biometrics;
using static SCI_PH.Persistence.Configurations.Sql;

namespace SCI_PH.Persistence.Configurations.Clinical.Biometrics
{
    // Applied explicitly by ClinicalDbContext (it is not a Domain entity). Status is a concurrency token, so of two
    // concurrent changes of a session (a result, a cancellation, a confirmation, an expiry) only one commits. The unique
    // filtered indexes keep one active session per emergency and one Pending session per reader, also under concurrent
    // requests. DeviceCredentialId has no foreign key: the reader lives in the identity schema.
    internal sealed class FingerprintIdentificationSessionConfiguration : IEntityTypeConfiguration<FingerprintIdentificationSessionEntry>
    {
        public const string ActivePerEncounterIndexName = "UX_FingerprintIdentificationSessions_ActivePerEncounter";
        public const string PendingPerReaderIndexName = "UX_FingerprintIdentificationSessions_PendingPerReader";

        public void Configure(EntityTypeBuilder<FingerprintIdentificationSessionEntry> builder)
        {
            const string status = nameof(FingerprintIdentificationSessionEntry.Status);
            const string matchedReference = nameof(FingerprintIdentificationSessionEntry.MatchedBiometricReferenceId);
            const string failureReason = nameof(FingerprintIdentificationSessionEntry.FailureReason);
            const string resultHash = nameof(FingerprintIdentificationSessionEntry.ResultHash);
            const string createdAt = nameof(FingerprintIdentificationSessionEntry.CreatedAtUtc);
            const string expiresAt = nameof(FingerprintIdentificationSessionEntry.ExpiresAtUtc);
            const string resolvedAt = nameof(FingerprintIdentificationSessionEntry.ResolvedAtUtc);
            const string confirmedAt = nameof(FingerprintIdentificationSessionEntry.ConfirmedAtUtc);

            builder.ToTable("FingerprintIdentificationSessions");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Status).IsConcurrencyToken();
            builder.Property(x => x.ResultHash).HasMaxLength(IdempotencyRequestHash.Length).IsFixedLength();

            builder.HasOne<ClinicalEncounter>().WithMany().HasForeignKey(x => x.ClinicalEncounterId);
            builder.HasOne<ClinicalStaff>().WithMany().HasForeignKey(x => x.RequestedByStaffId);
            builder.HasOne<PatientBiometricReference>().WithMany().HasForeignKey(x => x.MatchedBiometricReferenceId);

            builder.HasIndex(x => x.ClinicalEncounterId)
                .IsUnique()
                .HasDatabaseName(ActivePerEncounterIndexName)
                .HasFilter(In(status, FingerprintSession.ActiveStatuses));
            builder.HasIndex(x => x.DeviceCredentialId)
                .IsUnique()
                .HasDatabaseName(PendingPerReaderIndexName)
                .HasFilter(Is(status, FingerprintSessionStatus.Pending));

            builder.ToTable(table =>
            {
                table.HasCheckConstraint("CK_FingerprintIdentificationSessions_ResultHash",
                    Any(IsNull(resultHash), $"length({Column(resultHash)}) = {IdempotencyRequestHash.Length}"));
                table.HasCheckConstraint("CK_FingerprintIdentificationSessions_Dates", All(
                    After(expiresAt, createdAt),
                    NotBefore(resolvedAt, createdAt),
                    NotBefore(confirmedAt, resolvedAt)));
                // A result is a hash and a time; it leaves either a matched reference or a failure reason. Expired and
                // Cancelled keep the matched reference when they end a Matched session.
                table.HasCheckConstraint("CK_FingerprintIdentificationSessions_Lifecycle", All(
                    BothOrNeither(resolvedAt, resultHash),
                    Any(
                        All(Is(status, FingerprintSessionStatus.Pending), IsNull(resolvedAt), IsNull(matchedReference), IsNull(failureReason), IsNull(confirmedAt)),
                        All(Is(status, FingerprintSessionStatus.Matched), IsNotNull(resolvedAt), IsNotNull(matchedReference), IsNull(failureReason), IsNull(confirmedAt)),
                        All(In(status, [FingerprintSessionStatus.NoMatch, FingerprintSessionStatus.Failed]),
                            IsNotNull(resolvedAt), IsNull(matchedReference), IsNotNull(failureReason), IsNull(confirmedAt)),
                        All(Is(status, FingerprintSessionStatus.Confirmed), IsNotNull(resolvedAt), IsNotNull(matchedReference), IsNull(failureReason), IsNotNull(confirmedAt)),
                        All(In(status, [FingerprintSessionStatus.Expired, FingerprintSessionStatus.Cancelled]),
                            BothOrNeither(resolvedAt, matchedReference), IsNull(failureReason), IsNull(confirmedAt)))));
            });
        }
    }
}
