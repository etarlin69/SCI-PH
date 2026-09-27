using Microsoft.EntityFrameworkCore;

namespace SCI_PH.Persistence.Contexts
{
    public class ClinicalDbContext : DbContext
    {
        public const string Schema = "clinical";

        public ClinicalDbContext(DbContextOptions<ClinicalDbContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.HasDefaultSchema(Schema);
        }
    }
}
