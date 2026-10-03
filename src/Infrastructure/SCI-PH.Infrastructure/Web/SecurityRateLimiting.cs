using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SCI_PH.Infrastructure.Security;

namespace SCI_PH.Infrastructure.Web
{
    public static class RateLimitPolicies
    {
        // Login, refresh, MFA verification and invitation activation.
        public const string Authentication = "authentication";
        // Forgot/reset password and email confirmation.
        public const string AccountRecovery = "account-recovery";
        public const string Device = "device";
    }

    // Per client IP (and per device code for devices). Account lockout already limits guessing per account,
    // so login is not partitioned by identifier: that would reveal nothing new and allow cheap lockout abuse.
    public sealed class RateLimitingOptions
    {
        public const string SectionName = "RateLimiting";

        public int AuthenticationPermitLimit { get; init; } = 20;
        public int AuthenticationWindowSeconds { get; init; } = 60;
        public int AccountRecoveryPermitLimit { get; init; } = 5;
        public int AccountRecoveryWindowSeconds { get; init; } = 900;
        public int DevicePermitLimit { get; init; } = 120;
        public int DeviceWindowSeconds { get; init; } = 60;
    }

    public static class SecurityRateLimiting
    {
        public static IServiceCollection AddSecurityRateLimiting(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddOptions<RateLimitingOptions>().Bind(configuration.GetSection(RateLimitingOptions.SectionName));
            services.AddRateLimiter(limiter =>
            {
                limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                limiter.OnRejected = WriteProblemAsync;
                limiter.AddPolicy(RateLimitPolicies.Authentication, context =>
                    Window(context, ClientIp(context), o => (o.AuthenticationPermitLimit, o.AuthenticationWindowSeconds)));
                limiter.AddPolicy(RateLimitPolicies.AccountRecovery, context =>
                    Window(context, ClientIp(context), o => (o.AccountRecoveryPermitLimit, o.AccountRecoveryWindowSeconds)));
                limiter.AddPolicy(RateLimitPolicies.Device, context =>
                    Window(context, $"{context.Request.Headers[DeviceAuthenticationDefaults.DeviceIdHeader]}|{ClientIp(context)}", o => (o.DevicePermitLimit, o.DeviceWindowSeconds)));
            });

            return services;
        }

        // Behind a reverse proxy, configure ForwardedHeaders so RemoteIpAddress is the real client.
        private static string ClientIp(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        private static RateLimitPartition<string> Window(
            HttpContext context,
            string key,
            Func<RateLimitingOptions, (int PermitLimit, int WindowSeconds)> select)
        {
            var (permitLimit, windowSeconds) = select(context.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value);
            return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromSeconds(windowSeconds),
                QueueLimit = 0
            });
        }

        private static async ValueTask WriteProblemAsync(OnRejectedContext context, CancellationToken cancellationToken)
        {
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            {
                context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
            }

            var problems = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
            await problems.WriteAsync(new ProblemDetailsContext
            {
                HttpContext = context.HttpContext,
                ProblemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Too many requests. Try again later."
                }
            });
        }
    }
}
