using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SCI_PH.Application.Common.Security;
using SCI_PH.Persistence.Identity.Models;
using static SCI_PH.Persistence.Configurations.Sql;

namespace SCI_PH.Persistence.Configurations.Identity
{
    // No foreign keys: a device is not an account and is never linked to clinical data.
    internal sealed class DeviceCredentialConfiguration : IEntityTypeConfiguration<DeviceCredential>
    {
        public void Configure(EntityTypeBuilder<DeviceCredential> builder)
        {
            const string status = nameof(DeviceCredential.Status);
            const string revokedAt = nameof(DeviceCredential.RevokedAtUtc);

            builder.HasKey(x => x.Id);
            builder.Property(x => x.DeviceCode).HasMaxLength(SecurityFieldLengths.DeviceCode);
            builder.Property(x => x.Name).HasMaxLength(SecurityFieldLengths.DeviceName);
            builder.Property(x => x.Scopes).HasMaxLength(SecurityFieldLengths.DeviceScopes);
            builder.Property(x => x.SecretHash).HasMaxLength(SecurityFieldLengths.TokenHash).IsFixedLength();
            builder.Property(x => x.FirmwareVersion).HasMaxLength(SecurityFieldLengths.FirmwareVersion);

            builder.HasIndex(x => x.DeviceCode).IsUnique();

            builder.ToTable(table =>
            {
                table.HasCheckConstraint("CK_DeviceCredentials_Code",
                    $"{Column(nameof(DeviceCredential.DeviceCode))} ~ {Text("^[A-Z0-9][A-Z0-9-]{2,59}$")}");
                table.HasCheckConstraint("CK_DeviceCredentials_Hash",
                    $"{Column(nameof(DeviceCredential.SecretHash))} ~ {Text($"^[0-9a-f]{{{SecurityFieldLengths.TokenHash}}}$")}");
                table.HasCheckConstraint("CK_DeviceCredentials_Name", NotBlank(nameof(DeviceCredential.Name)));
                table.HasCheckConstraint("CK_DeviceCredentials_Scopes",
                    $"{Column(nameof(DeviceCredential.Scopes))} ~ {Text("^Device[.][A-Za-z.]+( Device[.][A-Za-z.]+)*$")}");
                table.HasCheckConstraint("CK_DeviceCredentials_Revocation", Any(
                    All(Is(status, DeviceCredentialStatus.Active), IsNull(revokedAt)),
                    All(Is(status, DeviceCredentialStatus.Revoked), IsNotNull(revokedAt))));
                table.HasCheckConstraint("CK_DeviceCredentials_Status_Enum",
                    In(status, Enum.GetValues<DeviceCredentialStatus>()));
            });
        }
    }
}
