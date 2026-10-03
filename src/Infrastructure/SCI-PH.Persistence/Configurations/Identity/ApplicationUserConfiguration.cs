using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SCI_PH.Persistence.Identity.Models;
using static SCI_PH.Persistence.Configurations.Sql;

namespace SCI_PH.Persistence.Configurations.Identity
{
    internal sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
    {
        public void Configure(EntityTypeBuilder<ApplicationUser> builder)
        {
            // Identity creates EmailIndex as non-unique; the database must also reject duplicate emails.
            builder.HasIndex(x => x.NormalizedEmail).HasDatabaseName("EmailIndex").IsUnique();

            builder.ToTable(table => table.HasCheckConstraint("CK_AspNetUsers_Deactivation",
                $"{IsTrue(nameof(ApplicationUser.IsActive))} = ({IsNull(nameof(ApplicationUser.DeactivatedAtUtc))})"));
        }
    }
}
