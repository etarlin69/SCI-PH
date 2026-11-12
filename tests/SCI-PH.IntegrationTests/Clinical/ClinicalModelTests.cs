using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using SCI_PH.Domain.Entities.Auditing;
using SCI_PH.Domain.Entities.Documents;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Entities.Medications;
using SCI_PH.Domain.Entities.Organizations;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.ValueObjects;
using SCI_PH.Persistence.Clinical.Biometrics;
using SCI_PH.Persistence.Clinical.Idempotency;
using SCI_PH.Persistence.Contexts;

namespace SCI_PH.IntegrationTests.Clinical
{
    // Provider model tests: no connection is opened and no database is required.
    // Behaviour against a real PostgreSQL instance (Testcontainers) is planned for a follow-up PR.
    public class ClinicalModelTests
    {
        private const string ModelOnlyConnectionString = "Host=localhost;Database=sciph_model_tests;Username=model_tests";

        private static ClinicalDbContext CreateContext() => new(new DbContextOptionsBuilder<ClinicalDbContext>()
            .UseNpgsql(ModelOnlyConnectionString)
            .Options);

        private static IEntityType Entity<T>(ClinicalDbContext context) => context.Model.FindEntityType(typeof(T))!;

        // Check constraints are design-time metadata and are not kept in the runtime model.
        private static IModel DesignModel(ClinicalDbContext context) => context.GetService<IDesignTimeModel>().Model;

        [Fact]
        public void MigrationSnapshotMatchesTheModel()
        {
            using var context = CreateContext();

            Assert.False(context.Database.HasPendingModelChanges(),
                "Run 'dotnet ef migrations add' for ClinicalDbContext: the model differs from the snapshot.");
        }

        // Domain entities, plus exactly one technical table: the idempotency receipts (ADR-008), kept in the clinical
        // schema so they commit in the same transaction as the clinical rows they protect.
        [Fact]
        public void AllEntitiesAreDomainEntitiesInTheClinicalSchema()
        {
            using var context = CreateContext();
            var entities = context.Model.GetEntityTypes().ToList();

            Assert.Equal(33, entities.Count);
            Assert.All(entities, entity => Assert.Equal(ClinicalDbContext.Schema, entity.GetSchema()));
            // Technical state committed with the clinical rows, not Domain entities.
            var technical = entities.Where(entity => !entity.ClrType.Namespace!.StartsWith("SCI_PH.Domain.Entities", StringComparison.Ordinal))
                .Select(entity => entity.ClrType).OrderBy(type => type.Name).ToList();
            Assert.Equal([typeof(FaceTemplateDeletionJobEntry), typeof(FingerprintEnrollmentSessionEntry), typeof(FingerprintIdentificationSessionEntry),
                typeof(FingerprintSlotDeletionJobEntry), typeof(IdempotencyReceiptEntry)], technical);
        }

        [Fact]
        public void EveryRelationshipRestrictsDeletesAndUsesExplicitKeys()
        {
            using var context = CreateContext();
            var foreignKeys = context.Model.GetEntityTypes().SelectMany(entity => entity.GetForeignKeys()).ToList();

            Assert.NotEmpty(foreignKeys);
            Assert.All(foreignKeys, key =>
            {
                Assert.Equal(DeleteBehavior.Restrict, key.DeleteBehavior);
                Assert.DoesNotContain(key.Properties, property => property.IsShadowProperty());
            });
        }

        [Fact]
        public void NoBinaryContentIsStored()
        {
            using var context = CreateContext();

            Assert.DoesNotContain(context.Model.GetEntityTypes().SelectMany(e => e.GetProperties()), p => p.ClrType == typeof(byte[]));
            Assert.DoesNotContain("bytea", context.Database.GenerateCreateScript());
        }

        [Fact]
        public void IdentityIsNotPartOfTheClinicalModel()
        {
            using var context = CreateContext();
            var ddl = context.Database.GenerateCreateScript();

            Assert.DoesNotContain("AspNet", ddl);
            Assert.DoesNotContain("identity.", ddl);
            Assert.Equal(typeof(Guid?), Entity<Patient>(context).FindProperty(nameof(Patient.IdentityUserId))!.ClrType);
            Assert.Empty(Entity<Patient>(context).GetForeignKeys());
        }

        [Fact]
        public void CheckConstraintsAndFiltersAreSingleLine()
        {
            using var context = CreateContext();
            var sql = DesignModel(context).GetEntityTypes()
                .SelectMany(entity => entity.GetCheckConstraints().Select(check => check.Sql)
                    .Concat(entity.GetIndexes().Select(index => index.GetFilter()).OfType<string>()))
                .ToList();

            Assert.NotEmpty(sql);
            Assert.All(sql, text => Assert.False(text.Contains('\n') || text.Contains('\r'), text));
        }

        [Fact]
        public void EveryEnumColumnHasAnAllowedValuesConstraint()
        {
            using var context = CreateContext();

            foreach (var entity in DesignModel(context).GetEntityTypes())
            {
                var checks = entity.GetCheckConstraints().Select(check => check.ModelName).ToHashSet();
                foreach (var property in entity.GetProperties().Where(p => (Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType).IsEnum))
                {
                    Assert.Contains($"CK_{entity.GetTableName()}_{property.GetColumnName()}_Enum", checks);
                }
            }
        }

        [Theory]
        [InlineData(typeof(PatientIdentifier), "UX_PatientIdentifiers_ActiveValue", "\"Type\" IN (1, 2, 3, 4)")]
        [InlineData(typeof(PatientIdentifier), "UX_PatientIdentifiers_ActivePrimary", "\"IsPrimary\"")]
        [InlineData(typeof(ClinicStaffAffiliation), "UX_ClinicStaffAffiliations_Active", "\"EndedAtUtc\" IS NULL")]
        [InlineData(typeof(EncounterStaffAssignment), "UX_EncounterStaffAssignments_Active", "\"UnassignedAtUtc\" IS NULL")]
        // Requested (1) and InTransit (2): ClinicalTransfer.ActiveStatuses.
        [InlineData(typeof(SCI_PH.Domain.Entities.Transport.ClinicalTransfer), "UX_ClinicalTransfers_ActivePerEncounter", "\"Status\" IN (1, 2)")]
        public void FilteredUniqueIndexesProtectActiveRecordsOnly(Type entityType, string indexName, string expectedFilter)
        {
            using var context = CreateContext();
            var index = Assert.Single(context.Model.FindEntityType(entityType)!.GetIndexes(), i => i.GetDatabaseName() == indexName);

            Assert.True(index.IsUnique);
            Assert.Contains(expectedFilter, index.GetFilter());
        }

        [Theory]
        [InlineData(typeof(ClinicalEncounter), nameof(ClinicalEncounter.TemporaryPatientCode))]
        [InlineData(typeof(ClinicalDocument), nameof(ClinicalDocument.BlobPath))]
        [InlineData(typeof(Clinic), nameof(Clinic.FacilityCode))]
        [InlineData(typeof(ClinicalStaff), nameof(ClinicalStaff.ProfessionalLicenseNumber))]
        [InlineData(typeof(ClinicalStaff), nameof(ClinicalStaff.IdentityUserId))]
        [InlineData(typeof(Patient), nameof(Patient.IdentityUserId))]
        [InlineData(typeof(SCI_PH.Domain.Entities.Transport.Ambulance), "Code")]
        [InlineData(typeof(Medication), nameof(Medication.Code))]
        public void BusinessKeysAreUnique(Type entityType, string property)
        {
            using var context = CreateContext();

            Assert.Contains(context.Model.FindEntityType(entityType)!.GetIndexes(),
                index => index.IsUnique && index.Properties.Select(p => p.Name).SequenceEqual([property]));
        }

        [Theory]
        [InlineData(typeof(VitalSign), typeof(Triage), "TriageId", "ClinicalEncounterId")]
        [InlineData(typeof(ClinicalResult), typeof(ClinicalProcedure), "ClinicalProcedureId", "ClinicalEncounterId")]
        [InlineData(typeof(IdentificationAttempt), typeof(PatientIdentifier), "PatientIdentifierId", "PatientId")]
        [InlineData(typeof(IdentificationAttempt), typeof(PatientBiometricReference), "BiometricReferenceId", "PatientId")]
        [InlineData(typeof(MedicationAdministration), typeof(Prescription), "PrescriptionId", "ClinicalEncounterId")]
        [InlineData(typeof(MedicationAdministration), typeof(PrescriptionDetail), "PrescriptionDetailId", "PrescriptionId")]
        public void OptionalLinksCannotCrossToAnotherEncounterOrPatient(Type dependent, Type principal, string first, string second)
        {
            using var context = CreateContext();
            var key = Assert.Single(context.Model.FindEntityType(dependent)!.GetForeignKeys(),
                key => key.PrincipalEntityType.ClrType == principal);

            Assert.Equal([first, second], key.Properties.Select(p => p.Name));
            Assert.False(key.IsRequired);
        }

        [Theory]
        [InlineData("CK_ClinicalEncounters_Identification")]
        [InlineData("CK_ClinicalTransfers_DifferentClinics")]
        [InlineData("CK_ClinicalTransfers_Lifecycle")]
        [InlineData("CK_ClinicalDocuments_Owner")]
        [InlineData("CK_ClinicalDocuments_Size")]
        [InlineData("CK_IdentificationAttempts_Outcome")]
        [InlineData("CK_ClinicalAuditEntries_Actor")]
        [InlineData("CK_ClinicalAuditEntries_Outcome")]
        [InlineData("CK_VitalSigns_Range")]
        public void CriticalRulesAreInThePostgresDdl(string constraintName)
        {
            using var context = CreateContext();

            Assert.Contains(constraintName, context.Database.GenerateCreateScript());
        }

        [Fact]
        public void EncounterDetectsConcurrentIdentificationAndStatusChanges()
        {
            using var context = CreateContext();
            var encounter = Entity<ClinicalEncounter>(context);

            Assert.True(encounter.FindProperty(nameof(ClinicalEncounter.PatientId))!.IsNullable);
            Assert.True(encounter.FindProperty(nameof(ClinicalEncounter.PatientId))!.IsConcurrencyToken);
            Assert.True(encounter.FindProperty(nameof(ClinicalEncounter.Status))!.IsConcurrencyToken);
        }

        [Fact]
        public void TransferDetectsConcurrentStatusChangesAndItsActiveStatusesMatchTheIndex()
        {
            using var context = CreateContext();
            var transfer = Entity<SCI_PH.Domain.Entities.Transport.ClinicalTransfer>(context);

            Assert.True(transfer.FindProperty(nameof(SCI_PH.Domain.Entities.Transport.ClinicalTransfer.Status))!.IsConcurrencyToken);
            Assert.Equal(
                [SCI_PH.Domain.Enums.Transport.ClinicalTransferStatus.Requested, SCI_PH.Domain.Enums.Transport.ClinicalTransferStatus.InTransit],
                SCI_PH.Domain.Entities.Transport.ClinicalTransfer.ActiveStatuses.Order());
            Assert.Equal((int)SCI_PH.Domain.Enums.Transport.ClinicalTransferStatus.Requested, 1);
            Assert.Equal((int)SCI_PH.Domain.Enums.Transport.ClinicalTransferStatus.InTransit, 2);
        }

        [Fact]
        public void AuditLogHasNoForeignKeys()
        {
            using var context = CreateContext();

            Assert.Empty(Entity<ClinicalAuditEntry>(context).GetForeignKeys());
        }

        [Theory]
        [InlineData(EntityState.Modified)]
        [InlineData(EntityState.Deleted)]
        public async Task AuditCannotBeChangedThroughSaveChanges(EntityState state)
        {
            using var context = CreateContext();
            var audit = ClinicalAuditEntry.Success(AuditActor.System("tests"), AuditActionType.Read, "Patient", "1", DateTimeOffset.UtcNow);
            context.Attach(audit);
            context.Entry(audit).State = state;

            Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }
    }
}
