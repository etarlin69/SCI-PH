using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using SCI_PH.Application;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.Application.Features.Accounts;
using SCI_PH.Application.Features.Authentication;
using SCI_PH.Domain.Entities.Organizations;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Organizations;
using SCI_PH.Domain.Enums.Patients;
using SCI_PH.Infrastructure;
using SCI_PH.Persistence;
using SCI_PH.Persistence.Contexts;
using SCI_PH.Persistence.Identity.Models;
using SCI_PH.Persistence.Identity.Seed;

namespace SCI_PH.IntegrationTests.Security.Support
{
    // Real composition (AddPersistence + AddJwtSecurity + AddAccountSecurity + AddAccountManagement) over in-memory
    // stores, or over a real PostgreSQL database when a connection string is given.
    // Each operation runs in its own scope, like separate HTTP requests.
    internal sealed class SecurityTestContext : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;

        public SecurityTestContext(
            IDictionary<string, string?>? settings = null,
            string? postgresConnectionString = null,
            RefreshReadBarrier? refreshReadBarrier = null)
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(TestSettings.Create(settings)).Build();

            var services = new ServiceCollection();
            services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(Logs));
            services.AddApplication().AddAccountSecurity().AddAccountManagement(configuration).AddDeviceSecurity();
            services.AddPersistence(configuration);
            services.AddInfrastructure(configuration).AddJwtSecurity(configuration);
            if (postgresConnectionString is null)
            {
                services.UseInMemoryStores($"security-{Guid.NewGuid():N}");
            }
            else
            {
                services.UsePostgresStores(postgresConnectionString);
            }

            services.UseFakeEmail(Email);
            services.AddSingleton<TimeProvider>(Time);
            if (refreshReadBarrier is not null)
            {
                services.AddRefreshReadBarrier(refreshReadBarrier);
            }
            _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        }

        public FakeTimeProvider Time { get; } = new(DateTimeOffset.UtcNow);

        public FakeEmailSender Email { get; } = new();

        // Every log line produced while the context is in use (Trace level, including EF Core SQL logging).
        public CapturingLoggerProvider Logs { get; } = new();

        public AsyncServiceScope Scope() => _provider.CreateAsyncScope();

        public async Task<T> RunAsync<T>(Func<IServiceProvider, Task<T>> action)
        {
            await using var scope = Scope();
            return await action(scope.ServiceProvider);
        }

        public async Task RunAsync(Func<IServiceProvider, Task> action)
        {
            await using var scope = Scope();
            await action(scope.ServiceProvider);
        }

        public Task<T> With<TService, T>(Func<TService, Task<T>> action) where TService : notnull =>
            RunAsync(sp => action(sp.GetRequiredService<TService>()));

        public Task With<TService>(Func<TService, Task> action) where TService : notnull =>
            RunAsync(sp => action(sp.GetRequiredService<TService>()));

        public Task<LoginResult?> LoginRawAsync(string identifier, string password) =>
            With<AuthenticationService, LoginResult?>(auth =>
                auth.LoginAsync(new LoginRequest(identifier, password), ClientContext.Unknown, CancellationToken.None));

        public async Task<AuthenticationResult?> LoginAsync(string identifier, string password) =>
            (await LoginRawAsync(identifier, password))?.Session;

        public Task<AuthenticationResult?> RefreshAsync(string refreshToken) =>
            With<AuthenticationService, AuthenticationResult?>(auth => auth.RefreshAsync(refreshToken, ClientContext.Unknown, CancellationToken.None));

        public Task<RefreshOutcome> RefreshSessionAsync(string refreshToken) =>
            With<AuthenticationService, RefreshOutcome>(auth => auth.RefreshSessionAsync(refreshToken, ClientContext.Unknown, CancellationToken.None));

        public Task<T> AuthenticationAsync<T>(Func<AuthenticationService, Task<T>> action) => With(action);

        public Task AuthenticationAsync(Func<AuthenticationService, Task> action) => With(action);

        public Task<T> AccountsAsync<T>(Func<UserAccountService, Task<T>> action) => With(action);

        public Task<T> DbAsync<T>(Func<IdentityDbContext, Task<T>> query) => With(query);

        public Task<T> ClinicalAsync<T>(Func<ClinicalDbContext, Task<T>> query) => With(query);

        public Task SeedAsync() => With<IdentitySeeder>(seeder => seeder.SeedAsync());

        public async Task<TestAccount> CreateUserAsync(params string[] roles)
        {
            await SeedAsync();
            var suffix = Guid.NewGuid().ToString("N")[..8];
            var account = new TestAccount($"user-{suffix}", $"user-{suffix}@sci-ph.test", TestSettings.NewPassword());

            await With<UserManager<ApplicationUser>>(async users =>
            {
                var user = ApplicationUser.Create(account.UserName, account.Email, Time.GetUtcNow());
                AssertSucceeded(await users.CreateAsync(user, account.Password));
                AssertSucceeded(await users.AddToRolesAsync(user, roles));
                account.Id = user.Id;
            });

            return account;
        }

        public Task<Guid> CreatePatientAsync(string? email = null) =>
            ClinicalAsync(async clinical =>
            {
                var patient = Patient.Register("Ana", "Pérez", new DateOnly(1990, 1, 1), BiologicalSex.Female);
                if (email is not null)
                {
                    patient.AddIdentifier(PatientIdentifierType.Email, email, PatientIdentifierSource.Manual, isPrimary: true);
                }

                clinical.Patients.Add(patient);
                await clinical.SaveChangesAsync();
                return patient.Id;
            });

        public Task<Guid> CreateStaffAsync(ClinicalStaffType type = ClinicalStaffType.Nurse) =>
            ClinicalAsync(async clinical =>
            {
                var staff = ClinicalStaff.Register("Rosa", "Díaz", type);
                clinical.ClinicalStaffMembers.Add(staff);
                await clinical.SaveChangesAsync();
                return staff.Id;
            });

        public ValueTask DisposeAsync() => _provider.DisposeAsync();

        private static void AssertSucceeded(IdentityResult result) =>
            Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(error => error.Code)));
    }

    internal sealed record TestAccount(string UserName, string Email, string Password)
    {
        public Guid Id { get; set; }
    }
}
