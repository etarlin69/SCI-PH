using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SCI_PH.Application.Common.Security;
using SCI_PH.Persistence.Identity.Models;
using static SCI_PH.Persistence.Configurations.Sql;

namespace SCI_PH.Persistence.Configurations.Identity
{
    internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
    {
        public void Configure(EntityTypeBuilder<RefreshToken> builder)
        {
            const string revokedAt = nameof(RefreshToken.RevokedAtUtc);
            const string reason = nameof(RefreshToken.RevocationReason);
            const string tokenHash = nameof(RefreshToken.TokenHash);

            builder.HasKey(x => x.Id);
            builder.Property(x => x.TokenHash).HasMaxLength(SecurityFieldLengths.TokenHash).IsFixedLength();
            builder.Property(x => x.CreatedByIp).HasMaxLength(SecurityFieldLengths.IpAddress);
            builder.Property(x => x.RevokedByIp).HasMaxLength(SecurityFieldLengths.IpAddress);
            // Revocation is a compare-and-set: a concurrent refresh of the same token fails instead of forking the session.
            builder.Property(x => x.RevokedAtUtc).IsConcurrencyToken();

            // Sessions have no value without their account, so they follow its deletion. Security history lives
            // in SecurityAuditEvents, which has no foreign key. Accounts are normally deactivated, not deleted.
            builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            builder.HasOne<RefreshToken>().WithMany().HasForeignKey(x => x.ReplacedByTokenId).OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(x => x.TokenHash).IsUnique();
            builder.HasIndex(x => new { x.UserId, x.ExpiresAtUtc });
            builder.HasIndex(x => new { x.UserId, x.RevokedAtUtc });
            builder.HasIndex(x => x.FamilyId);

            builder.ToTable(table =>
            {
                table.HasCheckConstraint("CK_RefreshTokens_Hash",
                    $"{Column(tokenHash)} ~ {Text($"^[0-9a-f]{{{SecurityFieldLengths.TokenHash}}}$")}");
                table.HasCheckConstraint("CK_RefreshTokens_Expiration",
                    After(nameof(RefreshToken.ExpiresAtUtc), nameof(RefreshToken.CreatedAtUtc)));
                table.HasCheckConstraint("CK_RefreshTokens_Revocation", All(
                    BothOrNeither(revokedAt, reason),
                    NotBefore(revokedAt, nameof(RefreshToken.CreatedAtUtc)),
                    Implies(IsNotNull(nameof(RefreshToken.ReplacedByTokenId)), Is(reason, RefreshTokenRevocationReason.Rotated))));
                table.HasCheckConstraint("CK_RefreshTokens_RevocationReason_Enum",
                    In(reason, Enum.GetValues<RefreshTokenRevocationReason>()));
            });
        }
    }
}
