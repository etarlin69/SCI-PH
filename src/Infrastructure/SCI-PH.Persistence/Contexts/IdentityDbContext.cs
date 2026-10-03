using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SCI_PH.Persistence.Identity.Models;

namespace SCI_PH.Persistence.Contexts
{
    // Also stores the ASP.NET Core Data Protection key ring, so email/reset tokens survive restarts and
    // are valid across Web, Mobile and Device hosts.
    public class IdentityDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>, IDataProtectionKeyContext
    {
        public const string Schema = "identity";

        public IdentityDbContext(DbContextOptions<IdentityDbContext> options)
            : base(options)
        {
        }

        public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
        public DbSet<SecurityAuditEvent> SecurityAuditEvents => Set<SecurityAuditEvent>();
        public DbSet<AccountInvitation> AccountInvitations => Set<AccountInvitation>();
        public DbSet<DeviceCredential> DeviceCredentials => Set<DeviceCredential>();
        public DbSet<MfaLoginChallenge> MfaLoginChallenges => Set<MfaLoginChallenge>();
        public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            EnsureSecurityAuditIsAppendOnly();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            EnsureSecurityAuditIsAppendOnly();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.HasDefaultSchema(Schema);
            builder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly, ConfiguresIdentityEntity);
        }

        // ExecuteUpdate, ExecuteDelete and raw SQL bypass this check; do not use them on security audit events.
        private void EnsureSecurityAuditIsAppendOnly()
        {
            if (ChangeTracker.Entries<SecurityAuditEvent>().Any(entry =>
                entry.State is EntityState.Modified or EntityState.Deleted))
            {
                throw new InvalidOperationException("Security audit events cannot be modified or deleted.");
            }
        }

        // Applies only configurations of Identity entities (this assembly), never clinical ones from Domain.
        private static bool ConfiguresIdentityEntity(Type configurationType) =>
            configurationType.GetInterfaces().Any(contract =>
                contract.IsGenericType
                && contract.GetGenericTypeDefinition() == typeof(IEntityTypeConfiguration<>)
                && contract.GenericTypeArguments[0].Assembly == typeof(IdentityDbContext).Assembly);
    }
}
