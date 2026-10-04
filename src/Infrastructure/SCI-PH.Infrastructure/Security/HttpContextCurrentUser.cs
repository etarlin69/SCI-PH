using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Security;

namespace SCI_PH.Infrastructure.Security
{
    internal sealed class HttpContextCurrentUser : ICurrentUser
    {
        private readonly IHttpContextAccessor _accessor;

        public HttpContextCurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

        private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

        public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

        public Guid? UserId =>
            IsAuthenticated && Guid.TryParse(Principal!.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : null;

        public IReadOnlyCollection<string> Roles => ClaimValues(CustomClaimTypes.Role);

        public IReadOnlyCollection<string> Permissions => ClaimValues(CustomClaimTypes.Permission);

        public bool HasPermission(string permission) =>
            IsAuthenticated && Principal!.HasClaim(CustomClaimTypes.Permission, permission);

        private string[] ClaimValues(string claimType) =>
            IsAuthenticated ? Principal!.FindAll(claimType).Select(claim => claim.Value).ToArray() : [];
    }
}
