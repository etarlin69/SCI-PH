using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using SCI_PH.Domain.Entities;
using SCI_PH.Persistence.Contexts;

namespace SCI_PH.IntegrationTests
{
    // Provider model tests: no connection is opened and no external database is required.
    public class ClinicalModelTests
    {
        private static ClinicalDbContext CreateContext() => new(new DbContextOptionsBuilder<ClinicalDbContext>()
            .UseNpgsql("Host=localhost;Database=sciph_model_tests;Username=model_tests")
            .Options);

        [Fact]
        public void AllClinicalEntitiesHaveExplicitForeignKeysAndStayInClinicalSchema()
        {
            using var context = CreateContext();
            foreach (var entity in context.Model.GetEntityTypes())
            {
                Assert.Equal("clinical", entity.GetSchema());
                Assert.Equal("SCI_PH.Domain.Entities", entity.ClrType.Namespace);
                foreach (var key in entity.GetForeignKeys())
                {
                    Assert.DoesNotContain(key.Properties, property => property.IsShadowProperty());
                    Assert.Equal(DeleteBehavior.Restrict, key.DeleteBehavior);
                }
            }
            Assert.Equal(typeof(Guid?), context.Model.FindEntityType(typeof(Patient))!.FindProperty("IdentityUserId")!.ClrType);
            Assert.Equal(typeof(Guid?), context.Model.FindEntityType(typeof(ClinicalStaff))!.FindProperty("IdentityUserId")!.ClrType);
            Assert.Equal(typeof(Guid), context.Model.FindEntityType(typeof(ClinicalAuditEntry))!.FindProperty("ActorIdentityUserId")!.ClrType);
        }

        [Fact]
        public void PhoneLookupIsNotGloballyUniqueButActiveIdentityIdentifiersAre()
        {
            using var context = CreateContext();
            var indexes = context.Model.FindEntityType(typeof(PatientIdentifier))!.GetIndexes().ToList();
            var uniqueIndex = Assert.Single(indexes, index => index.IsUnique);
            Assert.Equal(new[] { "Type", "Issuer", "NormalizedValue" }, uniqueIndex.Properties.Select(p => p.Name));
            Assert.Equal("\"Type\" IN (0, 1, 2, 3) AND \"RevokedAtUtc\" IS NULL", uniqueIndex.GetFilter());
            Assert.Contains(indexes, index => !index.IsUnique &&
                index.Properties.Select(p => p.Name).SequenceEqual(new[] { "Type", "NormalizedValue" }));
        }

        [Theory]
        [InlineData(typeof(VitalSign), typeof(TriageAssessment), "TriageAssessmentId", "ClinicalEncounterId")]
        [InlineData(typeof(ClinicalResult), typeof(ClinicalProcedure), "ClinicalProcedureId", "ClinicalEncounterId")]
        [InlineData(typeof(BiometricIdentificationAttempt), typeof(PatientBiometricReference), "BiometricReferenceId", "MatchedPatientId")]
        public void OptionalLinksCannotPointToAnotherEncounterOrPatient(Type dependent, Type principal, string first, string second)
        {
            using var context = CreateContext();
            var key = Assert.Single(context.Model.FindEntityType(dependent)!.GetForeignKeys(),
                key => key.PrincipalEntityType.ClrType == principal);
            Assert.Equal(new[] { first, second }, key.Properties.Select(p => p.Name));
            Assert.False(key.IsRequired);
        }

        [Fact]
        public void NnAndDocumentConstraintsAreIncludedInPostgresDdl()
        {
            using var context = CreateContext();
            var encounter = context.Model.FindEntityType(typeof(ClinicalEncounter))!;
            Assert.True(encounter.FindProperty("PatientId")!.IsNullable);
            Assert.True(encounter.FindProperty("PatientId")!.IsConcurrencyToken);
            var ddl = context.Database.GenerateCreateScript();
            Assert.Contains("CK_Encounter_Identification", ddl);
            Assert.Contains("CK_Document_Owner", ddl);
            Assert.Contains("CK_Document_Size", ddl);
            Assert.Contains("CK_Document_Checksum", ddl);
            Assert.DoesNotContain("CREATE TABLE identity.", ddl);
            Assert.DoesNotContain("bytea", ddl);
        }

        [Theory]
        [InlineData(EntityState.Modified)]
        [InlineData(EntityState.Deleted)]
        public async Task AuditCannotBeChangedThroughSaveChanges(EntityState state)
        {
            using var context = CreateContext();
            var audit = new ClinicalAuditEntry { ActorIdentityUserId = Guid.NewGuid(), EntityName = "Patient", EntityId = "test", Action = "Read" };
            context.Attach(audit);
            context.Entry(audit).State = state;
            Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }
    }
}
