using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SCI_PH.Persistence.Contexts;

namespace SCI_PH.Persistence.HealthChecks
{
    public sealed class PostgreSqlReadinessHealthCheck : IHealthCheck
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public PostgreSqlReadinessHealthCheck(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();

                var dbContext =
                    scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

                var canConnect =
                    await dbContext.Database.CanConnectAsync(cancellationToken);

                return canConnect
                    ? HealthCheckResult.Healthy("PostgreSQL is reachable.")
                    : HealthCheckResult.Unhealthy("PostgreSQL is unavailable.");
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                return HealthCheckResult.Unhealthy(
                    "PostgreSQL is unavailable.",
                    exception);
            }
        }
    }
}