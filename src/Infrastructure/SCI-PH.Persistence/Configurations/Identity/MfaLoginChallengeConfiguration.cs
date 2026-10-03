using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SCI_PH.Persistence.Identity.Models;
using static SCI_PH.Persistence.Configurations.Sql;

namespace SCI_PH.Persistence.Configurations.Identity
{
    internal sealed class MfaLoginChallengeConfiguration : IEntityTypeConfiguration<MfaLoginChallenge>
    {
        public void Configure(EntityTypeBuilder<MfaLoginChallenge> builder)
        {
            const string createdAt = nameof(MfaLoginChallenge.CreatedAtUtc);

            builder.HasKey(x => x.Id);
            builder.Property(x => x.ChallengeHash).HasMaxLength(SecurityFieldLengths.TokenHash).IsFixedLength();
            // Consumption is a compare-and-set: UPDATE ... WHERE "ConsumedAtUtc" IS NULL succeeds for one request only,
            // even across several backend instances.
            builder.Property(x => x.ConsumedAtUtc).IsConcurrencyToken();

            // Ephemeral login state with no history value: it follows the account. Security history lives in
            // SecurityAuditEvents, which has no foreign key.
            builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(x => x.ChallengeHash).IsUnique();
            builder.HasIndex(x => new { x.UserId, x.ExpiresAtUtc });

            builder.ToTable(table =>
            {
                table.HasCheckConstraint("CK_MfaLoginChallenges_Hash",
                    $"{Column(nameof(MfaLoginChallenge.ChallengeHash))} ~ {Text($"^[0-9a-f]{{{SecurityFieldLengths.TokenHash}}}$")}");
                table.HasCheckConstraint("CK_MfaLoginChallenges_Dates", All(
                    After(nameof(MfaLoginChallenge.ExpiresAtUtc), createdAt),
                    NotBefore(nameof(MfaLoginChallenge.ConsumedAtUtc), createdAt)));
            });
        }
    }
}
