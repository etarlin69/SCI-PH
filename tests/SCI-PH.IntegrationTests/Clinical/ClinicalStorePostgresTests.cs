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
using SCI_PH.Domain.Entities.Organizations;
using SCI_PH.Domain.Enums.Encounters;
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
                    new ClinicalDevelopmentStaff { Email = nurse.Email, Type = ClinicalStaffType.Nurse },
                    new ClinicalDevelopmentStaff { Email = admin.Email, Type = ClinicalStaffType.Physician },
                    new ClinicalDevelopmentStaff { Email = "missing@sci-ph.test" }
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
            var staff = await host.DbAsync(db => db.ClinicalStaffMembers.SingleAsync());
            Assert.Equal(nurse.Id, staff.IdentityUserId);
            Assert.Equal(1, await host.DbAsync(db => db.ClinicStaffAffiliations.CountAsync(a => a.ClinicalStaffId == staff.Id && a.EndedAtUtc == null)));

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
