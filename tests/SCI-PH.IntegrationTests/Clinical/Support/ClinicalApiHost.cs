using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.Domain.Entities.Medications;
using SCI_PH.Domain.Entities.Organizations;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Medications;
using SCI_PH.Domain.Enums.Organizations;
using SCI_PH.Domain.Enums.Patients;
using SCI_PH.IntegrationTests.PostgreSql;
using SCI_PH.IntegrationTests.Security.Support;
using SCI_PH.Persistence.Contexts;
using SCI_PH.Web.BFF.Controllers;

namespace SCI_PH.IntegrationTests.Clinical.Support
{
    // The real Web BFF over a migrated PostgreSQL database (Testcontainers): concurrency tokens, unique indexes,
    // composite foreign keys and transactions behave as in production. Every log line is captured (Trace), so a test can
    // check that nothing sensitive was written. Extra settings override the test defaults (e.g. a real face provider).
    internal sealed class PostgresWebBffFactory(
        string connectionString, CapturingLoggerProvider logs, IReadOnlyDictionary<string, string?>? extraSettings = null) : BffFactory<AuthController>
    {
        protected override IDictionary<string, string?> Settings
        {
            get
            {
                var settings = base.Settings;
                foreach (var (key, value) in extraSettings ?? new Dictionary<string, string?>())
                {
                    settings[key] = value;
                }

                return settings;
            }
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
            builder.ConfigureTestServices(services => services.UsePostgresStores(connectionString));
        }
    }

    internal sealed record StaffSession(HttpClient Client, Guid UserId, Guid? StaffId);

    internal sealed class ClinicalApiHost : IAsyncDisposable
    {
        private readonly PostgresWebBffFactory _factory;

        private ClinicalApiHost(string connectionString, IReadOnlyDictionary<string, string?>? settings = null)
        {
            ConnectionString = connectionString;
            _factory = new PostgresWebBffFactory(connectionString, Logs, settings);
        }

        public string ConnectionString { get; }

        // Log lines of the Web BFF.
        public CapturingLoggerProvider Logs { get; } = new();

        public IServiceProvider Services => _factory.Services;

        public PostgresWebBffFactory Factory => _factory;

        public static async Task<ClinicalApiHost> StartAsync(PostgresFixture postgres, IReadOnlyDictionary<string, string?>? settings = null) =>
            new(await postgres.CreateMigratedDatabaseAsync(), settings);

        public HttpClient Anonymous() => _factory.CreateHttpsClient();

        public async Task<Clinic> CreateClinicAsync(string name = "Hospital de prueba")
        {
            var clinic = Clinic.Register(name, ClinicType.Hospital, "Santo Domingo", "Distrito Nacional");
            await DbAsync(async db =>
            {
                db.Clinics.Add(clinic);
                await db.SaveChangesAsync();
            });
            return clinic;
        }

        // Identity account with the role + (optionally) ClinicalStaff linked to it and affiliated with the clinics.
        public async Task<StaffSession> SignInAsync(
            string role,
            Clinic[] clinics,
            bool asClinicalStaff = true,
            bool staffActive = true,
            ClinicalStaffType staffType = ClinicalStaffType.OtherClinical)
        {
            var (account, staffId) = await CreateStaffAccountAsync(role, clinics, asClinicalStaff, staffActive, staffType);
            var login = await _factory.CreateHttpsClient().PostAsJsonAsync("/api/web/auth/login", new LoginRequest(account.Email, account.Password));
            login.EnsureSuccessStatusCode();
            var session = await login.ReadAsAsync<WebLoginResponse>();
            return new StaffSession(_factory.CreateHttpsClient().WithBearer(session.Session!.AccessToken), account.Id, staffId);
        }

        // The account without signing in, for another host over the same database (e.g. the Mobile BFF).
        public async Task<(TestAccount Account, Guid? StaffId)> CreateStaffAccountAsync(
            string role,
            Clinic[] clinics,
            bool asClinicalStaff = true,
            bool staffActive = true,
            ClinicalStaffType staffType = ClinicalStaffType.OtherClinical)
        {
            var account = await _factory.CreateUserAsync(role);
            Guid? staffId = null;
            if (asClinicalStaff)
            {
                var staff = ClinicalStaff.Register("Personal", role, staffType);
                staff.LinkIdentityUser(account.Id);
                if (!staffActive)
                {
                    staff.Deactivate();
                }

                await DbAsync(async db =>
                {
                    db.ClinicalStaffMembers.Add(staff);
                    db.ClinicStaffAffiliations.AddRange(
                        clinics.Select(c => ClinicStaffAffiliation.Start(c.Id, staff.Id, DateTimeOffset.UtcNow.AddDays(-1))));
                    await db.SaveChangesAsync();
                });
                staffId = staff.Id;
            }

            return (account, staffId);
        }

        // Real web login of an existing account (e.g. one created through an invitation).
        public async Task<HttpClient> LoginAsync(string email, string password)
        {
            var login = await _factory.CreateHttpsClient().PostAsJsonAsync("/api/web/auth/login", new LoginRequest(email, password));
            login.EnsureSuccessStatusCode();
            var session = await login.ReadAsAsync<WebLoginResponse>();
            return _factory.CreateHttpsClient().WithBearer(session.Session!.AccessToken);
        }

        public async Task<Patient> SeedPatientAsync(string givenNames = "Ana", string familyNames = "Pérez", string? nationalId = null)
        {
            var patient = Patient.Register(givenNames, familyNames, new DateOnly(1985, 3, 14), BiologicalSex.Female);
            patient.AddIdentifier(PatientIdentifierType.NationalId, nationalId ?? RandomNationalId(), PatientIdentifierSource.Manual, isPrimary: true);
            await DbAsync(async db =>
            {
                db.Patients.Add(patient);
                await db.SaveChangesAsync();
            });
            return patient;
        }

        public async Task<Medication> SeedMedicationAsync(string name = "Paracetamol", bool active = true, MedicationRoute? defaultRoute = MedicationRoute.Oral)
        {
            var medication = Medication.Register(name, "Tableta", "500 mg", $"TEST-{Guid.NewGuid():N}"[..24], defaultRoute);
            if (!active)
            {
                medication.Deactivate();
            }

            await DbAsync(async db =>
            {
                db.Medications.Add(medication);
                await db.SaveChangesAsync();
            });
            return medication;
        }

        public static string RandomNationalId() => $"{Random.Shared.Next(100, 999)}-{Random.Shared.Next(1000000, 9999999)}-{Random.Shared.Next(0, 9)}";

        public async Task DbAsync(Func<ClinicalDbContext, Task> action)
        {
            await using var db = PostgresFixture.Clinical(ConnectionString);
            await action(db);
        }

        public async Task<T> DbAsync<T>(Func<ClinicalDbContext, Task<T>> action)
        {
            await using var db = PostgresFixture.Clinical(ConnectionString);
            return await action(db);
        }

        public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
    }
}
