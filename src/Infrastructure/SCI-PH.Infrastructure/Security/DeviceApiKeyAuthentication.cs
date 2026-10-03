using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.Application.Features.Devices;

namespace SCI_PH.Infrastructure.Security
{
    public static class DeviceAuthenticationDefaults
    {
        public const string Scheme = "DeviceApiKey";
        public const string DeviceIdHeader = "X-Device-Id";
        public const string DeviceKeyHeader = "X-Device-Key";
        public const string SectionName = "DeviceAuthentication";
    }

    public sealed class DeviceApiKeyAuthenticationOptions : AuthenticationSchemeOptions
    {
        // Keys must never travel in clear text. Only a local development profile may relax this.
        public bool RequireHttps { get; set; } = true;
    }

    // Reads the device code and key from headers only (never the query string) and builds a device principal:
    // device_id, device_code and device_scope claims, without any human role or clinical permission.
    internal sealed class DeviceApiKeyAuthenticationHandler : AuthenticationHandler<DeviceApiKeyAuthenticationOptions>
    {
        private readonly DeviceAuthenticationService _devices;

        public DeviceApiKeyAuthenticationHandler(
            IOptionsMonitor<DeviceApiKeyAuthenticationOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            DeviceAuthenticationService devices)
            : base(options, logger, encoder)
        {
            _devices = devices;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var deviceCode = Request.Headers[DeviceAuthenticationDefaults.DeviceIdHeader].ToString();
            var deviceKey = Request.Headers[DeviceAuthenticationDefaults.DeviceKeyHeader].ToString();
            if (string.IsNullOrEmpty(deviceCode) || string.IsNullOrEmpty(deviceKey))
            {
                return AuthenticateResult.NoResult();
            }

            if (Options.RequireHttps && !Request.IsHttps)
            {
                return AuthenticateResult.Fail("Device credentials require HTTPS.");
            }

            var client = new ClientContext(Context.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString());
            var device = await _devices.AuthenticateAsync(deviceCode, deviceKey, client, Context.RequestAborted);
            if (device is null)
            {
                return AuthenticateResult.Fail("Invalid device credentials.");
            }

            var claims = new List<Claim>
            {
                new(CustomClaimTypes.DeviceId, device.Id.ToString()),
                new(CustomClaimTypes.DeviceCode, device.DeviceCode)
            };
            claims.AddRange(device.Scopes.Select(scope => new Claim(CustomClaimTypes.DeviceScope, scope)));

            var identity = new ClaimsIdentity(claims, Scheme.Name, CustomClaimTypes.DeviceCode, roleType: null);
            return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
        }
    }
}
