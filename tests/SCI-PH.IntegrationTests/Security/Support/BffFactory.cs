using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SCI_PH.Infrastructure.Web;
using SCI_PH.Persistence.Identity.Models;
using SCI_PH.Persistence.Identity.Seed;

namespace SCI_PH.IntegrationTests.Security.Support
{
    // Runs a real host (Web, Mobile or Device) with in-memory stores and a fake email sender.
    // "Testing" avoids loading the developer's User Secrets. TEntryPoint is any type of the host assembly.
    public class BffFactory<TEntryPoint> : WebApplicationFactory<TEntryPoint> where TEntryPoint : class
    {
        private readonly string _databaseName = $"bff-{Guid.NewGuid():N}";

        internal FakeEmailSender Email { get; } = new();

        protected virtual string Environment => "Testing";

        protected virtual IDictionary<string, string?> Settings => TestSettings.Create();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environment);
            foreach (var (key, value) in Settings)
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureTestServices(services => services.UseInMemoryStores(_databaseName).UseFakeEmail(Email));
        }

        // HTTPS base address so Secure cookies and HTTPS-only device credentials behave as in production.
        public HttpClient CreateHttpsClient(bool handleCookies = false)
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                HandleCookies = handleCookies
            });
            client.DefaultRequestHeaders.Add(FrontendCors.CsrfHeaderName, "1");
            return client;
        }

        internal async Task<TestAccount> CreateUserAsync(params string[] roles)
        {
            var suffix = Guid.NewGuid().ToString("N")[..8];
            var account = new TestAccount($"user-{suffix}", $"user-{suffix}@sci-ph.test", TestSettings.NewPassword());
            await using var scope = Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedAsync();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = ApplicationUser.Create(account.UserName, account.Email, DateTimeOffset.UtcNow);
            Assert.True((await users.CreateAsync(user, account.Password)).Succeeded);
            Assert.True((await users.AddToRolesAsync(user, roles)).Succeeded);
            account.Id = user.Id;
            return account;
        }
    }

    internal static class HttpTestExtensions
    {
        public static async Task<T> ReadAsAsync<T>(this HttpResponseMessage response) =>
            (await response.Content.ReadFromJsonAsync<T>(new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)))!;

        public static HttpClient WithBearer(this HttpClient client, string accessToken)
        {
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            return client;
        }
    }
}
