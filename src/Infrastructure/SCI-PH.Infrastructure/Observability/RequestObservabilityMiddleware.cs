using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace SCI_PH.Infrastructure.Observability
{
    public sealed class RequestObservabilityMiddleware
    {
        public const string CorrelationHeaderName = "X-Correlation-ID";

        private readonly RequestDelegate _next;
        private readonly ILogger<RequestObservabilityMiddleware> _logger;

        public RequestObservabilityMiddleware(
            RequestDelegate next,
            ILogger<RequestObservabilityMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var stopwatch = Stopwatch.StartNew();

            var traceId = Activity.Current?.TraceId.ToString();
            var correlationId = string.IsNullOrWhiteSpace(traceId)
                ? context.TraceIdentifier
                : traceId;

            context.Response.OnStarting(() =>
            {
                context.Response.Headers[CorrelationHeaderName] = correlationId;
                return Task.CompletedTask;
            });

            using var scope = _logger.BeginScope(
                new Dictionary<string, object>
                {
                    ["CorrelationId"] = correlationId,
                    ["TraceId"] = traceId ?? correlationId
                });

            try
            {
                await _next(context);
            }
            finally
            {
                stopwatch.Stop();

                if (!context.Request.Path.StartsWithSegments("/health"))
                {
                    var statusCode = context.Response.StatusCode;

                    if (statusCode >= StatusCodes.Status500InternalServerError)
                    {
                        _logger.LogWarning(
                            "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMilliseconds} ms. CorrelationId={CorrelationId}",
                            context.Request.Method,
                            context.Request.Path.Value,
                            statusCode,
                            stopwatch.Elapsed.TotalMilliseconds,
                            correlationId);
                    }
                    else
                    {
                        _logger.LogInformation(
                            "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMilliseconds} ms. CorrelationId={CorrelationId}",
                            context.Request.Method,
                            context.Request.Path.Value,
                            statusCode,
                            stopwatch.Elapsed.TotalMilliseconds,
                            correlationId);
                    }
                }
            }
        }
    }

    public static class RequestObservabilityExtensions
    {
        public static IApplicationBuilder UseRequestObservability(
            this IApplicationBuilder app)
        {
            return app.UseMiddleware<RequestObservabilityMiddleware>();
        }
    }
}
