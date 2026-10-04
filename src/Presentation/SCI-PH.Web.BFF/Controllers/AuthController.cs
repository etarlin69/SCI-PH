using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.Application.Features.Authentication;
using SCI_PH.Infrastructure.Web;
using SCI_PH.Web.BFF.Security;

namespace SCI_PH.Web.BFF.Controllers
{
    // Web session: the access token is returned in JSON; the refresh token only travels in the HttpOnly cookie.
    public sealed record WebSession(string AccessToken, DateTimeOffset AccessTokenExpiresAtUtc, UserAccount User);

    public sealed record WebLoginResponse(bool MfaRequired, WebSession? Session, MfaChallenge? MfaChallenge);

    // Transport only: all rules live in AuthenticationService (shared with the Mobile BFF).
    [ApiController]
    [Route("api/web/auth")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class AuthController : ControllerBase
    {
        private const string AuthenticationFailed = "Authentication failed.";

        private readonly AuthenticationService _authentication;
        private readonly ICurrentUser _currentUser;

        public AuthController(AuthenticationService authentication, ICurrentUser currentUser)
        {
            _authentication = authentication;
            _currentUser = currentUser;
        }

        [AllowAnonymous]
        [CookieCsrfProtection]
        [EnableRateLimiting(RateLimitPolicies.Authentication)]
        [HttpPost("login")]
        [ProducesResponseType<WebLoginResponse>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
        {
            var result = await _authentication.LoginAsync(request, HttpContext.ToClientContext(), cancellationToken);
            if (result is null)
            {
                return AuthenticationFailedProblem();
            }

            return Ok(result.RequiresMfa
                ? new WebLoginResponse(true, null, result.Challenge)
                : new WebLoginResponse(false, StartSession(result.Session!), null));
        }

        [AllowAnonymous]
        [CookieCsrfProtection]
        [EnableRateLimiting(RateLimitPolicies.Authentication)]
        [HttpPost("mfa/verify")]
        [ProducesResponseType<WebSession>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> VerifyMfa(MfaVerifyRequest request, CancellationToken cancellationToken)
        {
            var session = await _authentication.VerifyMfaAsync(request, HttpContext.ToClientContext(), cancellationToken);
            return session is null ? AuthenticationFailedProblem() : Ok(StartSession(session));
        }

        // The refresh token is read exclusively from the cookie; a token in the body is ignored.
        [AllowAnonymous]
        [CookieCsrfProtection]
        [EnableRateLimiting(RateLimitPolicies.Authentication)]
        [HttpPost("refresh")]
        [ProducesResponseType<WebSession>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
        {
            var refreshToken = RefreshTokenCookie.Read(Request);
            var session = refreshToken is null ? null : await _authentication.RefreshAsync(refreshToken, HttpContext.ToClientContext(), cancellationToken);
            if (session is null)
            {
                RefreshTokenCookie.Delete(Response);
                return AuthenticationFailedProblem();
            }

            return Ok(StartSession(session));
        }

        [AllowAnonymous]
        [CookieCsrfProtection]
        [HttpPost("logout")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> Logout(CancellationToken cancellationToken)
        {
            if (RefreshTokenCookie.Read(Request) is { } refreshToken)
            {
                await _authentication.LogoutAsync(refreshToken, HttpContext.ToClientContext(), cancellationToken);
            }

            RefreshTokenCookie.Delete(Response);
            return NoContent();
        }

        [HttpGet("me")]
        [ProducesResponseType<UserAccount>(StatusCodes.Status200OK)]
        public async Task<IActionResult> Me(CancellationToken cancellationToken)
        {
            var account = await _authentication.GetCurrentAccountAsync(_currentUser.UserId!.Value, cancellationToken);
            return account is null ? AuthenticationFailedProblem() : Ok(account);
        }

        [HttpPost("change-password")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
        {
            var result = await _authentication.ChangePasswordAsync(_currentUser.UserId!.Value, request, HttpContext.ToClientContext(), cancellationToken);
            if (result.Succeeded)
            {
                RefreshTokenCookie.Delete(Response);
                return NoContent();
            }

            if (result.FailureReason == SecurityFailureReason.PasswordMismatch)
            {
                ModelState.AddModelError(nameof(request.CurrentPassword), "The current password is incorrect.");
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(nameof(request.NewPassword), error);
            }

            return ValidationProblem(ModelState);
        }

        [HttpPost("revoke-sessions")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> RevokeSessions(CancellationToken cancellationToken)
        {
            await _authentication.RevokeAllSessionsAsync(_currentUser.UserId!.Value, HttpContext.ToClientContext(), cancellationToken);
            RefreshTokenCookie.Delete(Response);
            return NoContent();
        }

        private WebSession StartSession(AuthenticationResult session)
        {
            RefreshTokenCookie.Write(Response, session.RefreshToken, session.RefreshTokenExpiresAtUtc);
            return new WebSession(session.AccessToken, session.AccessTokenExpiresAtUtc, session.User);
        }

        private ObjectResult AuthenticationFailedProblem() => Problem(statusCode: StatusCodes.Status401Unauthorized, title: AuthenticationFailed);
    }
}
