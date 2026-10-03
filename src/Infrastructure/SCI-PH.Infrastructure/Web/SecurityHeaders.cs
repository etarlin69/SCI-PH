using Microsoft.AspNetCore.Builder;

namespace SCI_PH.Infrastructure.Web
{
    // Minimal headers for JSON APIs. No CSP: these hosts serve no HTML. HSTS and HTTPS redirection are
    // configured per host; if a reverse proxy adds the same headers, remove them there or here, not both.
    public static class SecurityHeaders
    {
        public static IApplicationBuilder UseApiSecurityHeaders(this IApplicationBuilder app) =>
            app.Use(async (context, next) =>
            {
                context.Response.OnStarting(() =>
                {
                    var headers = context.Response.Headers;
                    headers["X-Content-Type-Options"] = "nosniff";
                    headers["Referrer-Policy"] = "no-referrer";
                    headers["X-Frame-Options"] = "DENY";
                    return Task.CompletedTask;
                });

                await next(context);
            });
    }
}
