using SCI_PH.Application.DTOs.Security;

namespace SCI_PH.Web.BFF.Security
{
    public static class HttpContextExtensions
    {
        // IP and User-Agent for the security audit (truncated when stored). Never headers with credentials.
        public static ClientContext ToClientContext(this HttpContext context) =>
            new(context.Connection.RemoteIpAddress?.ToString(), context.Request.Headers.UserAgent.ToString());
    }
}
