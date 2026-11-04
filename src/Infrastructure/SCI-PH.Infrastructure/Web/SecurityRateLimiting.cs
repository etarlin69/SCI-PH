using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
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
        // Assisted clinical AI generation: per authenticated user (window + concurrency).
        public const string ClinicalAi = "clinical-ai";
        // Patient self-service reads (Mobile BFF): abuse protection per authenticated user, not a clinical throughput limit.
        public const string PatientSelfService = "patient-self-service";
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
        public int ClinicalAiPermitLimit { get; init; } = 10;
        public int ClinicalAiWindowSeconds { get; init; } = 60;
        public int ClinicalAiConcurrentRequests { get; init; } = 1;
        public int PatientSelfServicePermitLimit { get; init; } = 60;
        public int PatientSelfServiceWindowSeconds { get; init; } = 60;
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
                limiter.AddPolicy(RateLimitPolicies.ClinicalAi, ClinicalAiPartition);
                limiter.AddPolicy(RateLimitPolicies.PatientSelfService, context =>
                    Window(context, UserPartition(context), o => (o.PatientSelfServicePermitLimit, o.PatientSelfServiceWindowSeconds)));
            });

            return services;
        }

        // Behind a reverse proxy, configure ForwardedHeaders so RemoteIpAddress is the real client.
        private static string ClientIp(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // The authenticated user (the JWT "sub", so UseAuthentication must run before UseRateLimiter). A request without a
        // user is partitioned by IP; authorization rejects it anyway.
        private static string UserPartition(HttpContext context)
        {
            var userId = context.User.Identity?.IsAuthenticated == true ? context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value : null;
            return userId is null ? $"anonymous|{ClientIp(context)}" : $"user|{userId}";
        }

        // Per authenticated user: at most N requests per window AND M at the same time.
        private static RateLimitPartition<string> ClinicalAiPartition(HttpContext context)
        {
            var options = context.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
            return RateLimitPartition.Get(UserPartition(context), _ => new WindowAndConcurrencyLimiter(
                new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.ClinicalAiPermitLimit,
                    Window = TimeSpan.FromSeconds(options.ClinicalAiWindowSeconds),
                    QueueLimit = 0
                }),
                new ConcurrencyLimiter(new ConcurrencyLimiterOptions
                {
                    PermitLimit = options.ClinicalAiConcurrentRequests,
                    QueueLimit = 0
                })));
        }

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

    // Both limits of one partition must grant a permit. The concurrency permit is taken first, so a request rejected for
    // being concurrent does not spend a window permit; its rejection says when to retry (1 s), like the window's.
    internal sealed class WindowAndConcurrencyLimiter(FixedWindowRateLimiter window, ConcurrencyLimiter concurrency) : RateLimiter
    {
        private static readonly TimeSpan _concurrentRetryAfter = TimeSpan.FromSeconds(1);

        public override TimeSpan? IdleDuration =>
            window.IdleDuration is { } w && concurrency.IdleDuration is { } c ? (w < c ? w : c) : null;

        public override RateLimiterStatistics? GetStatistics() => concurrency.GetStatistics();

        protected override RateLimitLease AttemptAcquireCore(int permitCount) =>
            Combine(concurrency.AttemptAcquire(permitCount), () => window.AttemptAcquire(permitCount));

        protected override async ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
        {
            var concurrent = await concurrency.AcquireAsync(permitCount, cancellationToken);
            return concurrent.IsAcquired
                ? Combine(concurrent, await window.AcquireAsync(permitCount, cancellationToken))
                : Rejected(concurrent);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                window.Dispose();
                concurrency.Dispose();
            }

            base.Dispose(disposing);
        }

        private static RateLimitLease Combine(RateLimitLease concurrent, Func<RateLimitLease> acquireWindow) =>
            concurrent.IsAcquired ? Combine(concurrent, acquireWindow()) : Rejected(concurrent);

        private static RateLimitLease Combine(RateLimitLease concurrent, RateLimitLease windowed)
        {
            if (windowed.IsAcquired)
            {
                return new BothLease(concurrent, windowed);
            }

            concurrent.Dispose();
            return windowed;
        }

        private static RateLimitLease Rejected(RateLimitLease concurrent)
        {
            concurrent.Dispose();
            return new RetryLaterLease(_concurrentRetryAfter);
        }

        private sealed class BothLease(RateLimitLease first, RateLimitLease second) : RateLimitLease
        {
            public override bool IsAcquired => true;

            public override IEnumerable<string> MetadataNames => [];

            public override bool TryGetMetadata(string metadataName, out object? metadata)
            {
                metadata = null;
                return false;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    first.Dispose();
                    second.Dispose();
                }

                base.Dispose(disposing);
            }
        }

        private sealed class RetryLaterLease(TimeSpan retryAfter) : RateLimitLease
        {
            public override bool IsAcquired => false;

            public override IEnumerable<string> MetadataNames => [MetadataName.RetryAfter.Name];

            public override bool TryGetMetadata(string metadataName, out object? metadata)
            {
                metadata = metadataName == MetadataName.RetryAfter.Name ? retryAfter : null;
                return metadata is not null;
            }
        }
    }
}
