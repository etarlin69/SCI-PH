using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using SCI_PH.Persistence.Contexts;
using SCI_PH.Persistence.Identity.Models;

namespace SCI_PH.IntegrationTests.Security
{
    // Provider model tests: no connection is opened.
    public class IdentityModelTests
    {
        private static IdentityDbContext CreateContext() => new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql("Host=localhost;Database=sciph_model_tests;Username=model_tests")
            .Options);

        private static IModel DesignModel(DbContext context) => context.GetService<IDesignTimeModel>().Model;

        [Fact]
        public void MigrationSnapshotMatchesTheModel()
        {
            using var context = CreateContext();

            Assert.False(context.Database.HasPendingModelChanges(),
                "Run 'dotnet ef migrations add' for IdentityDbContext: the model differs from the snapshot.");
        }

        [Fact]
        public void IdentityModelContainsOnlyAccountAndSecurityTables()
        {
            using var context = CreateContext();
            var tables = context.Model.GetEntityTypes().Select(e => e.GetTableName()!).Order(StringComparer.Ordinal).ToArray();

            Assert.Equal(
                new[] { "AccountInvitations", "AspNetRoleClaims", "AspNetRoles", "AspNetUserClaims", "AspNetUserLogins", "AspNetUserRoles", "AspNetUserTokens", "AspNetUsers", "DataProtectionKeys", "DeviceCredentials", "MfaLoginChallenges", "RefreshTokens", "SecurityAuditEvents" }.Order(StringComparer.Ordinal),
                tables);
            Assert.All(context.Model.GetEntityTypes(), e => Assert.Equal(IdentityDbContext.Schema, e.GetSchema()));
            Assert.DoesNotContain(context.Model.GetEntityTypes(), e => e.ClrType.Namespace?.StartsWith("SCI_PH.Domain", StringComparison.Ordinal) == true);
        }

        [Fact]
        public void EmailAndRefreshTokenHashAreUnique()
        {
            using var context = CreateContext();

            var email = Assert.Single(context.Model.FindEntityType(typeof(ApplicationUser))!.GetIndexes(), i => i.GetDatabaseName() == "EmailIndex");
            Assert.True(email.IsUnique);
            Assert.Contains(context.Model.FindEntityType(typeof(RefreshToken))!.GetIndexes(),
                i => i.IsUnique && i.Properties.Single().Name == nameof(RefreshToken.TokenHash));
        }

        [Fact]
        public void SecurityAuditSurvivesAccountsAndRefreshTokensFollowThem()
        {
            using var context = CreateContext();

            Assert.Empty(context.Model.FindEntityType(typeof(SecurityAuditEvent))!.GetForeignKeys());
            var userKey = Assert.Single(context.Model.FindEntityType(typeof(RefreshToken))!.GetForeignKeys(),
                k => k.PrincipalEntityType.ClrType == typeof(ApplicationUser));
            Assert.Equal(DeleteBehavior.Cascade, userKey.DeleteBehavior);
            Assert.True(context.Model.FindEntityType(typeof(RefreshToken))!.FindProperty(nameof(RefreshToken.RevokedAtUtc))!.IsConcurrencyToken);
        }

        [Fact]
        public void CheckConstraintsAreSingleLine()
        {
            using var context = CreateContext();
            var checks = DesignModel(context).GetEntityTypes().SelectMany(e => e.GetCheckConstraints()).Select(c => c.Sql).ToList();

            Assert.NotEmpty(checks);
            Assert.All(checks, sql => Assert.False(sql.Contains('\n') || sql.Contains('\r'), sql));
        }

        [Theory]
        [InlineData(typeof(ApplicationUser))]
        [InlineData(typeof(RefreshToken))]
        [InlineData(typeof(SecurityAuditEvent))]
        [InlineData(typeof(AccountInvitation))]
        [InlineData(typeof(DeviceCredential))]
        public void SecurityEntitiesHoldNoClinicalData(Type type)
        {
            string[] clinicalTerms = ["Patient", "Diagnos", "Allerg", "Biometric", "Medical", "Encounter", "Clinical", "Prescription", "Result", "Document", "NationalId", "Cedula"];

            Assert.DoesNotContain(type.GetProperties(), property =>
                clinicalTerms.Any(term => property.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
                || property.PropertyType.Namespace?.StartsWith("SCI_PH.Domain", StringComparison.Ordinal) == true
                || property.PropertyType == typeof(byte[]));
        }

        [Fact]
        public void IdentityDdlDoesNotTouchTheClinicalSchema()
        {
            using var context = CreateContext();
            var ddl = context.Database.GenerateCreateScript();

            Assert.DoesNotContain("clinical.", ddl);
            Assert.Contains("CK_AspNetUsers_Deactivation", ddl);
            Assert.Contains("CK_RefreshTokens_Revocation", ddl);
            Assert.Contains("CK_SecurityAuditEvents_Outcome", ddl);
        }
    }
}
