using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using SCI_PH.Infrastructure.Web;

namespace SCI_PH.Web.BFF.Security
{
    // Protects the endpoints that read or set the refresh cookie (login, MFA verify, refresh, logout).
    // Besides SameSite=Strict and strict CORS: the request must carry the custom X-SCIPH-CSRF header (a cross-site
    // form cannot set it, and a cross-site script needs a CORS preflight that only allowed origins pass) and, when
    // the browser sends Origin, it must be an allowed origin or this host.
    public sealed class CookieCsrfProtectionAttribute : TypeFilterAttribute
    {
        public CookieCsrfProtectionAttribute() : base(typeof(CookieCsrfProtectionFilter))
        {
        }

        private sealed class CookieCsrfProtectionFilter : IAsyncActionFilter
        {
            private readonly FrontendSecurityOptions _frontend;

            public CookieCsrfProtectionFilter(IOptions<FrontendSecurityOptions> frontend) => _frontend = frontend.Value;

            public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
            {
                var request = context.HttpContext.Request;
                if (request.Headers[FrontendCors.CsrfHeaderName] != "1" || !IsAllowedOrigin(request))
                {
                    context.Result = new ObjectResult(new ProblemDetails
                    {
                        Status = StatusCodes.Status403Forbidden,
                        Title = "Request rejected by CSRF protection."
                    })
                    { StatusCode = StatusCodes.Status403Forbidden };
                    return Task.CompletedTask;
                }

                return next();
            }

            private bool IsAllowedOrigin(HttpRequest request)
            {
                var origin = request.Headers.Origin.ToString();
                return string.IsNullOrEmpty(origin)
                    || origin.Equals($"{request.Scheme}://{request.Host}", StringComparison.OrdinalIgnoreCase)
                    || _frontend.AllowedOrigins.Contains(origin.TrimEnd('/'), StringComparer.OrdinalIgnoreCase);
            }
        }
    }
}
