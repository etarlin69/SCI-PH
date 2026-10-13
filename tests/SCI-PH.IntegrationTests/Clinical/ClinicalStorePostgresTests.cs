using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Security;
using SCI_PH.Domain.Enums.Organizations;
using SCI_PH.Persistence;
using SCI_PH.Persistence.Clinical.Seed;
using SCI_PH.Persistence.Identity.Models;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Entities.Medications;
using SCI_PH.Domain.Entities.Organizations;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Enums.Medications;
using SCI_PH.Domain.Enums.Patients;
using SCI_PH.IntegrationTests.Clinical.Support;
using SCI_PH.IntegrationTests.PostgreSql;

namespace SCI_PH.IntegrationTests.Clinical
{
    // The Persistence implementations of the clinical stores against real PostgreSQL constraints.
    [Collection(PostgresCollection.Name)]
    public sealed class ClinicalStorePostgresTests
    {
        private static readonly CancellationToken None = CancellationToken.None;
        private readonly PostgresFixture _postgres;

        public ClinicalStorePostgresTests(PostgresFixture postgres) => _postgres = postgres;

        [SkippableFact]
        public async Task ConcurrencyTokenTurnsALostIdentificationIntoAConflict()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await ClinicalApiHost.StartAsync(_postgres);
            var (clinic, staff) = await ClinicWithStaffAsync(host);
            var (first, second) = (await host.SeedPatientAsync(), await host.SeedPatientAsync());
            var encounter = await AddEncounterAsync(host, clinic.Id);

            await using var scopeA = host.Services.CreateAsyncScope();
            await using var scopeB = host.Services.CreateAsyncScope();
            var storeA = scopeA.ServiceProvider.GetRequiredService<IClinicalWriteStore>();
            var storeB = scopeB.ServiceProvider.GetRequiredService<IClinicalWriteStore>();
            var loadedA = (await storeA.FindEncounterAsync(encounter.Id, None))!;
            var loadedB = (await storeB.FindEncounterAsync(encounter.Id, None))!;
            loadedA.IdentifyPatient(first.Id, staff.Id, DateTimeOffset.UtcNow);
            loadedB.IdentifyPatient(second.Id, staff.Id, DateTimeOffset.UtcNow);

            await storeA.SaveChangesAsync(None);
            await Assert.ThrowsAsync<ResourceConflictException>(() => storeB.SaveChangesAsync(None));

            Assert.Equal(first.Id, await host.DbAsync(db => db.ClinicalEncounters.Where(e => e.Id == encounter.Id).Select(e => e.PatientId).SingleAsync()));
        }

        [SkippableFact]
        public async Task AVitalSignPointingToAnotherEncountersTriageIsRejectedAndNothingIsSaved()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await ClinicalApiHost.StartAsync(_postgres);
            var (clinic, staff) = await ClinicWithStaffAsync(host);
            var encounterA = await AddEncounterAsync(host, clinic.Id);
            var encounterB = await AddEncounterAsync(host, clinic.Id);
            var now = DateTimeOffset.UtcNow;
            var triageOfA = Triage.Record(encounterA.Id, TriagePriority.Urgent, staff.Id, now);
            await host.DbAsync(async db =>
            {
                db.Triages.Add(triageOfA);
                await db.SaveChangesAsync();
            });

            await using var scope = host.Services.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IClinicalWriteStore>();
            var loadedB = (await store.FindEncounterAsync(encounterB.Id, None))!;
            loadedB.ChangeStatus(ClinicalEncounterStatus.InProgress, now);
            var triageOfB = Triage.Record(encounterB.Id, TriagePriority.Standard, staff.Id, now);
            store.Add(triageOfB);
            store.Add(VitalSign.Record(encounterB.Id, VitalSignType.HeartRate, 80, now, staff.Id, triageOfB.Id));
            // Same encounter id, triage of another encounter: the composite foreign key must reject it.
            store.Add(VitalSign.Record(encounterB.Id, VitalSignType.HeartRate, 81, now, staff.Id, triageOfA.Id));

            var error = await Assert.ThrowsAsync<DbUpdateException>(() => store.SaveChangesAsync(None));

            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
            // One transaction: the triage, the valid reading and the status change were rolled back too.
            Assert.False(await host.DbAsync(db => db.Triages.AnyAsync(t => t.ClinicalEncounterId == encounterB.Id)));
            Assert.False(await host.DbAsync(db => db.VitalSigns.AnyAsync(v => v.ClinicalEncounterId == encounterB.Id)));
            Assert.Equal(ClinicalEncounterStatus.Created,
                await host.DbAsync(db => db.ClinicalEncounters.Where(e => e.Id == encounterB.Id).Select(e => e.Status).SingleAsync()));
        }

        [SkippableFact]
        public async Task TemporaryPatientCodesAreUnique()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await ClinicalApiHost.StartAsync(_postgres);
            var (clinic, _) = await ClinicWithStaffAsync(host);
            var first = await AddEncounterAsync(host, clinic.Id);
            var second = await AddEncounterAsync(host, clinic.Id);

            Assert.NotEqual(first.TemporaryPatientCode, second.TemporaryPatientCode);
            var error = await Assert.ThrowsAsync<PostgresException>(() => host.DbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE clinical.\"ClinicalEncounters\" SET \"TemporaryPatientCode\" = {first.TemporaryPatientCode!.Value} WHERE \"Id\" = {second.Id}")));
            Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
        }

        [SkippableFact]
        public async Task ReadStoreFindsByNormalizedValueAndReturnsTheLatestTriage()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await ClinicalApiHost.StartAsync(_postgres);
            var (clinic, staff) = await ClinicWithStaffAsync(host);
            var patient = await host.SeedPatientAsync(nationalId: "402-1234567-8");
            var encounter = await AddEncounterAsync(host, clinic.Id, patient.Id);
            var now = DateTimeOffset.UtcNow;
            await host.DbAsync(async db =>
            {
                db.Triages.Add(Triage.Record(encounter.Id, TriagePriority.Immediate, staff.Id, now.AddMinutes(-2)));
                db.Triages.Add(Triage.Record(encounter.Id, TriagePriority.NonUrgent, staff.Id, now.AddMinutes(-1)));
                await db.SaveChangesAsync();
            });

            await using var scope = host.Services.CreateAsyncScope();
            var reads = scope.ServiceProvider.GetRequiredService<IClinicalReadStore>();
            var found = Assert.Single(await reads.FindPatientsByIdentifierAsync(PatientIdentifierType.NationalId, "40212345678", 10, None));
            var row = (await reads.GetEncounterAsync(encounter.Id, None))!;

            Assert.Equal(patient.Id, found.Id);
            Assert.Equal("40212345678", found.PrimaryIdentifier!.NormalizedValue);
            Assert.Empty(await reads.FindPatientsByIdentifierAsync(PatientIdentifierType.Passport, "40212345678", 10, None));
            Assert.Equal(TriagePriority.NonUrgent, row.LatestTriagePriority);
            Assert.Equal(patient.Id, row.Patient!.Id);
        }

        [SkippableFact]
        public async Task StaffDirectoryOnlyCountsOpenAffiliationsToActiveClinics()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await ClinicalApiHost.StartAsync(_postgres);
            var (active, staff) = await ClinicWithStaffAsync(host);
            var ended = await host.CreateClinicAsync("Clínica anterior");
            var closed = await host.CreateClinicAsync("Clínica cerrada");
            await host.DbAsync(async db =>
            {
                var past = ClinicStaffAffiliation.Start(ended.Id, staff.Id, DateTimeOffset.UtcNow.AddYears(-1));
                past.End(DateTimeOffset.UtcNow.AddMonths(-1));
                db.ClinicStaffAffiliations.AddRange(past, ClinicStaffAffiliation.Start(closed.Id, staff.Id, DateTimeOffset.UtcNow.AddDays(-1)));
                var inactiveClinic = await db.Clinics.SingleAsync(c => c.Id == closed.Id);
                inactiveClinic.Deactivate();
                await db.SaveChangesAsync();
            });

            await using var scope = host.Services.CreateAsyncScope();
            var membership = (await scope.ServiceProvider.GetRequiredService<IClinicalStaffDirectory>()
                .FindByIdentityUserAsync(staff.IdentityUserId!.Value, None))!;

            Assert.Equal(active.Id, Assert.Single(membership.ActiveClinicIds));
        }

        [SkippableFact]
        public async Task DevelopmentSeedIsIdempotentSkipsAdministratorsAndNeverRunsOutsideDevelopment()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await ClinicalApiHost.StartAsync(_postgres);
            var nurse = await host.Factory.CreateUserAsync(SecurityRoles.Nurse);
            var admin = await host.Factory.CreateUserAsync(SecurityRoles.Administrator);
            var options = Options.Create(new ClinicalDevelopmentSeedOptions
            {
                Enabled = true,
                Staff =
                [
                    new ClinicalDevelopmentStaff { LicenseNumber = "dev-nur-01", Email = nurse.Email, Type = ClinicalStaffType.Nurse },
                    new ClinicalDevelopmentStaff { LicenseNumber = "DEV-DOC-01", Email = admin.Email, Type = ClinicalStaffType.Physician },
                    new ClinicalDevelopmentStaff { LicenseNumber = "DEV-REC-01", Email = "missing@sci-ph.test" }
                ]
            });

            for (var run = 0; run < 2; run++)
            {
                await using var scope = host.Services.CreateAsyncScope();
                await new ClinicalDevelopmentSeeder(
                    scope.ServiceProvider.GetRequiredService<SCI_PH.Persistence.Contexts.ClinicalDbContext>(),
                    scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
                    options,
                    TimeProvider.System,
                    NullLogger<ClinicalDevelopmentSeeder>.Instance).SeedAsync();
            }

            Assert.Equal(1, await host.DbAsync(db => db.Clinics.CountAsync()));
            var staff = await host.DbAsync(db => db.ClinicalStaffMembers.OrderBy(s => s.ProfessionalLicenseNumber).ToListAsync());
            Assert.Equal(["DEV-DOC-01", "DEV-NUR-01", "DEV-REC-01"], staff.Select(s => s.ProfessionalLicenseNumber));
            Assert.Equal(nurse.Id, staff.Single(s => s.ProfessionalLicenseNumber == "DEV-NUR-01").IdentityUserId);
            // The administrator is never linked; unknown e-mails leave the record ready for an invitation.
            Assert.Null(staff.Single(s => s.ProfessionalLicenseNumber == "DEV-DOC-01").IdentityUserId);
            Assert.Null(staff.Single(s => s.ProfessionalLicenseNumber == "DEV-REC-01").IdentityUserId);
            Assert.Equal(3, await host.DbAsync(db => db.ClinicStaffAffiliations.CountAsync(a => a.EndedAtUtc == null)));
            // Demo medication catalog: created once (by code), active, with "DEV-" codes.
            var catalog = await host.DbAsync(db => db.Medications.ToListAsync());
            Assert.Equal(6, catalog.Count);
            Assert.All(catalog, m => Assert.StartsWith("DEV-", m.Code));
            Assert.All(catalog, m => Assert.Equal(MedicationStatus.Active, m.Status));

            // Entry point used by Program.cs: with the option enabled it seeds in Development and does nothing elsewhere.
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SciPhDatabase"] = host.ConnectionString,
                ["ClinicalDevelopmentSeed:Enabled"] = "true",
                ["ClinicalDevelopmentSeed:FacilityCode"] = "DEV-ENV-GUARD"
            }).Build();
            await using var provider = new ServiceCollection().AddLogging().AddPersistence(configuration).BuildServiceProvider();
            foreach (var name in new[] { Environments.Production, Environments.Staging, "Testing" })
            {
                await provider.RunClinicalDevelopmentSeedAsync(new TestEnvironment(name));
            }

            Assert.False(await host.DbAsync(db => db.Clinics.AnyAsync(c => c.FacilityCode == "DEV-ENV-GUARD")));
            await provider.RunClinicalDevelopmentSeedAsync(new TestEnvironment(Environments.Development));
            Assert.True(await host.DbAsync(db => db.Clinics.AnyAsync(c => c.FacilityCode == "DEV-ENV-GUARD")));
            Assert.Equal(6, await host.DbAsync(db => db.Medications.CountAsync()));
        }

        // The administration store turns the known uniqueness violations into conflicts (409), never server errors:
        // an open affiliation per staff member and clinic, an account linked to one staff member, unique facility code and
        // license number. Both writers passed any Application check; PostgreSQL decides.
        [SkippableFact]
        public async Task TheOrganizationStoreReportsKnownDuplicatesAsConflicts()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await ClinicalApiHost.StartAsync(_postgres);
            var (_, staff) = await ClinicWithStaffAsync(host);
            var other = ClinicalStaff.Register("Otra", "Persona", ClinicalStaffType.Nurse);
            await host.DbAsync(async db =>
            {
                db.ClinicalStaffMembers.Add(other);
                await db.SaveChangesAsync();
            });

            async Task<ResourceConflictException> ConflictAsync(Action<IClinicalOrganizationStore, IClinicalOrganizationStore> write)
            {
                await using var scopeA = host.Services.CreateAsyncScope();
                await using var scopeB = host.Services.CreateAsyncScope();
                var storeA = scopeA.ServiceProvider.GetRequiredService<IClinicalOrganizationStore>();
                var storeB = scopeB.ServiceProvider.GetRequiredService<IClinicalOrganizationStore>();
                write(storeA, storeB);
                await storeA.SaveChangesAsync(None);
                return await Assert.ThrowsAsync<ResourceConflictException>(() => storeB.SaveChangesAsync(None));
            }

            // ClinicWithStaffAsync already opened staff@clinic: start another clinic for a clean race.
            var second = await host.CreateClinicAsync("Hospital carrera");
            var affiliation = await ConflictAsync((a, b) =>
            {
                a.Add(ClinicStaffAffiliation.Start(second.Id, staff.Id, DateTimeOffset.UtcNow));
                b.Add(ClinicStaffAffiliation.Start(second.Id, staff.Id, DateTimeOffset.UtcNow));
            });
            Assert.Equal(1, await host.DbAsync(db => db.ClinicStaffAffiliations.CountAsync(a => a.ClinicId == second.Id && a.EndedAtUtc == null)));

            var license = await ConflictAsync((a, b) =>
            {
                a.Add(ClinicalStaff.Register("A", "B", ClinicalStaffType.Physician, "LIC-RACE-1"));
                b.Add(ClinicalStaff.Register("C", "D", ClinicalStaffType.Physician, "lic-race-1"));
            });
            var facility = await ConflictAsync((a, b) =>
            {
                a.Add(Clinic.Register("H1", ClinicType.Hospital, "P", "M", "FAC-RACE-1"));
                b.Add(Clinic.Register("H2", ClinicType.Hospital, "P", "M", "fac-race-1"));
            });

            // The same account cannot be linked to two staff members.
            var account = staff.IdentityUserId!.Value;
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var store = scope.ServiceProvider.GetRequiredService<IClinicalOrganizationStore>();
                (await store.FindStaffAsync(other.Id, None))!.LinkIdentityUser(account);
                var linked = await Assert.ThrowsAsync<ResourceConflictException>(() => store.SaveChangesAsync(None));
                Assert.Equal("The account is already linked to another staff member.", linked.Message);
            }

            Assert.Equal("The staff member already has an open affiliation with this clinic.", affiliation.Message);
            Assert.Equal("A staff member with this professional license number already exists.", license.Message);
            Assert.Equal("A clinic with this facility code already exists.", facility.Message);
            Assert.Null(await host.DbAsync(db => db.ClinicalStaffMembers.Where(s => s.Id == other.Id).Select(s => s.IdentityUserId).SingleAsync()));
        }

        // Last line of defence of "one active transfer per encounter": two writers that both passed the Application check
        // (both read "no active transfer") reach PostgreSQL; the filtered unique index lets only one active transfer in and
        // the store reports the other as a conflict (409), not a server error. Finished transfers do not block.
        [SkippableFact]
        public async Task TheDatabaseAllowsOnlyOneActiveTransferPerEncounter()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await ClinicalApiHost.StartAsync(_postgres);
            var (origin, _) = await ClinicWithStaffAsync(host);
            var destination = await host.CreateClinicAsync("Hospital destino");
            var encounter = await AddEncounterAsync(host, origin.Id);

            await using var scopeA = host.Services.CreateAsyncScope();
            await using var scopeB = host.Services.CreateAsyncScope();
            var storeA = scopeA.ServiceProvider.GetRequiredService<IClinicalWriteStore>();
            var storeB = scopeB.ServiceProvider.GetRequiredService<IClinicalWriteStore>();
            var first = Domain.Entities.Transport.ClinicalTransfer.Request(encounter.Id, destination.Id, DateTimeOffset.UtcNow, origin.Id);
            var second = Domain.Entities.Transport.ClinicalTransfer.Request(encounter.Id, destination.Id, DateTimeOffset.UtcNow, origin.Id);
            storeA.Add(first);
            storeB.Add(second);

            await storeA.SaveChangesAsync(None);
            var conflict = await Assert.ThrowsAsync<ResourceConflictException>(() => storeB.SaveChangesAsync(None));

            Assert.Equal("The encounter already has an active transfer.", conflict.Message);
            Assert.Equal(1, await host.DbAsync(db => db.ClinicalTransfers.CountAsync(t => t.ClinicalEncounterId == encounter.Id)));

            // A cancelled transfer no longer blocks a new one.
            await host.DbAsync(async db =>
            {
                (await db.ClinicalTransfers.SingleAsync(t => t.Id == first.Id)).Cancel(DateTimeOffset.UtcNow);
                await db.SaveChangesAsync();
                db.ClinicalTransfers.Add(Domain.Entities.Transport.ClinicalTransfer.Request(encounter.Id, destination.Id, DateTimeOffset.UtcNow, origin.Id));
                await db.SaveChangesAsync();
            });
            Assert.Equal(2, await host.DbAsync(db => db.ClinicalTransfers.CountAsync(t => t.ClinicalEncounterId == encounter.Id)));
        }

        // The store writes a prescription with its details in one save; the composite keys reject an administration that
        // points to a prescription of another encounter, and nothing of that save is stored.
        [SkippableFact]
        public async Task PrescriptionDetailsAreSavedTogetherAndCompositeKeysKeepAdministrationsInTheirEncounter()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await ClinicalApiHost.StartAsync(_postgres);
            var (clinic, staff) = await ClinicWithStaffAsync(host);
            var encounterA = await AddEncounterAsync(host, clinic.Id);
            var encounterB = await AddEncounterAsync(host, clinic.Id);
            var medication = await host.SeedMedicationAsync();
            var prescription = Prescription.Draft(encounterA.Id, staff.Id);
            var detail = prescription.AddDetail(medication.Id, 500, "mg", MedicationRoute.Oral, "cada 8 horas", 3);
            prescription.Issue(DateTimeOffset.UtcNow);

            await using (var scope = host.Services.CreateAsyncScope())
            {
                var store = scope.ServiceProvider.GetRequiredService<IClinicalWriteStore>();
                store.Add(prescription);
                await store.SaveChangesAsync(None);
            }

            Assert.Equal(detail.Id, await host.DbAsync(db => db.PrescriptionDetails.Where(d => d.PrescriptionId == prescription.Id).Select(d => d.Id).SingleAsync()));
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var store = scope.ServiceProvider.GetRequiredService<IClinicalWriteStore>();
                store.Add(MedicationAdministration.Record(
                    encounterB.Id, medication.Id, 500, "mg", MedicationRoute.Oral, DateTimeOffset.UtcNow, staff.Id, prescription.Id, detail.Id));
                var exception = await Assert.ThrowsAsync<DbUpdateException>(() => store.SaveChangesAsync(None));
                Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(exception.InnerException).SqlState);
            }

            Assert.False(await host.DbAsync(db => db.MedicationAdministrations.AnyAsync()));
        }

        private sealed class TestEnvironment(string name) : IHostEnvironment
        {
            public string EnvironmentName { get; set; } = name;
            public string ApplicationName { get; set; } = "SCI-PH.Tests";
            public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
            public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
                new Microsoft.Extensions.FileProviders.NullFileProvider();
        }

        private static async Task<(Clinic Clinic, ClinicalStaff Staff)> ClinicWithStaffAsync(ClinicalApiHost host)
        {
            var clinic = await host.CreateClinicAsync();
            var staff = ClinicalStaff.Register("Ana", "Enfermera", Domain.Enums.Organizations.ClinicalStaffType.Nurse);
            staff.LinkIdentityUser(Guid.NewGuid());
            await host.DbAsync(async db =>
            {
                db.ClinicalStaffMembers.Add(staff);
                db.ClinicStaffAffiliations.Add(ClinicStaffAffiliation.Start(clinic.Id, staff.Id, DateTimeOffset.UtcNow.AddDays(-1)));
                await db.SaveChangesAsync();
            });
            return (clinic, staff);
        }

        private static async Task<ClinicalEncounter> AddEncounterAsync(ClinicalApiHost host, Guid clinicId, Guid? patientId = null)
        {
            var startedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
            var encounter = patientId is { } id
                ? ClinicalEncounter.CreateForPatient(id, ClinicalEncounterType.Emergency, startedAt, clinicId)
                : ClinicalEncounter.CreateUnidentified(ClinicalEncounterType.Emergency, startedAt, clinicId);
            await host.DbAsync(async db =>
            {
                db.ClinicalEncounters.Add(encounter);
                await db.SaveChangesAsync();
            });
            return encounter;
        }
    }
}
