using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SCI_PH.Application.Common.Security;
using SCI_PH.Persistence.Identity.Models;
using static SCI_PH.Persistence.Configurations.Sql;

namespace SCI_PH.Persistence.Configurations.Identity
{
    internal sealed class AccountInvitationConfiguration : IEntityTypeConfiguration<AccountInvitation>
    {
        public void Configure(EntityTypeBuilder<AccountInvitation> builder)
        {
            const string status = nameof(AccountInvitation.Status);
            const string subjectType = nameof(AccountInvitation.SubjectType);
            const string role = nameof(AccountInvitation.IntendedRole);
            const string acceptedAt = nameof(AccountInvitation.AcceptedAtUtc);
            const string acceptedBy = nameof(AccountInvitation.AcceptedByUserId);
            const string revokedAt = nameof(AccountInvitation.RevokedAtUtc);

            builder.HasKey(x => x.Id);
            builder.Property(x => x.Email).HasMaxLength(SecurityFieldLengths.Email);
            builder.Property(x => x.IntendedRole).HasMaxLength(SecurityFieldLengths.RoleName);
            builder.Property(x => x.TokenHash).HasMaxLength(SecurityFieldLengths.TokenHash).IsFixedLength();
            // Claiming is a compare-and-set on Status: an invitation can be used only once.
            builder.Property(x => x.Status).IsConcurrencyToken();

            builder.HasIndex(x => x.TokenHash).IsUnique();
            // At most one pending invitation per clinical record; issuing a new one revokes the previous.
            builder.HasIndex(x => new { x.SubjectType, x.SubjectId })
                .IsUnique()
                .HasDatabaseName("UX_AccountInvitations_PendingSubject")
                .HasFilter(Is(status, AccountInvitationStatus.Pending));
            builder.HasIndex(x => x.Email);

            builder.ToTable(table =>
            {
                table.HasCheckConstraint("CK_AccountInvitations_Hash",
                    $"{Column(nameof(AccountInvitation.TokenHash))} ~ {Text($"^[0-9a-f]{{{SecurityFieldLengths.TokenHash}}}$")}");
                table.HasCheckConstraint("CK_AccountInvitations_Email", NotBlank(nameof(AccountInvitation.Email)));
                table.HasCheckConstraint("CK_AccountInvitations_Expiration",
                    After(nameof(AccountInvitation.ExpiresAtUtc), nameof(AccountInvitation.CreatedAtUtc)));
                // Patients can only become Patient accounts; staff never Patient nor Administrator.
                table.HasCheckConstraint("CK_AccountInvitations_Role", Any(
                    All(Is(subjectType, InvitationSubjectType.Patient), $"{Column(role)} = {Text(SecurityRoles.Patient)}"),
                    All(Is(subjectType, InvitationSubjectType.ClinicalStaff),
                        $"{Column(role)} IN ({string.Join(", ", SecurityRoles.InvitableStaff.Select(Text))})")));
                table.HasCheckConstraint("CK_AccountInvitations_Lifecycle", Any(
                    All(Is(status, AccountInvitationStatus.Pending), IsNull(acceptedAt), IsNull(acceptedBy), IsNull(revokedAt)),
                    All(Is(status, AccountInvitationStatus.Accepted), IsNotNull(acceptedAt), IsNull(revokedAt)),
                    All(Is(status, AccountInvitationStatus.Revoked), IsNotNull(revokedAt), IsNull(acceptedBy))));
                table.HasCheckConstraint("CK_AccountInvitations_SubjectType_Enum",
                    In(subjectType, Enum.GetValues<InvitationSubjectType>()));
                table.HasCheckConstraint("CK_AccountInvitations_Status_Enum",
                    In(status, Enum.GetValues<AccountInvitationStatus>()));
            });
        }
    }
}
