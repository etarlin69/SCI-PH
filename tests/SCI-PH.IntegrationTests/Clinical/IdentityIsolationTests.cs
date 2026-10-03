using Microsoft.EntityFrameworkCore;
using SCI_PH.Persistence.Contexts;

namespace SCI_PH.IntegrationTests.Clinical
{
    public class IdentityIsolationTests
    {
        [Fact]
        public void IdentityContextStaysInItsOwnSchemaWithoutClinicalTables()
        {
            using var context = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>()
                .UseNpgsql("Host=localhost;Database=sciph_model_tests;Username=model_tests")
                .Options);

            Assert.All(context.Model.GetEntityTypes(), entity =>
            {
                Assert.Equal(IdentityDbContext.Schema, entity.GetSchema());
                Assert.False(entity.ClrType.Namespace?.StartsWith("SCI_PH.Domain", StringComparison.Ordinal) ?? false);
            });
        }
    }
}
