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

        // ProblemDetails "code" of the retryable 409 of /refresh; the SPA retries only on this code.
        public const string RefreshSupersededCode = "refresh-superseded";

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
        // - 200: rotated; the new token replaces the cookie.
        // - 401: invalid, expired, revoked or replayed token; the cookie is deleted.
        // - 409 (code refresh-superseded): another request (another tab of the same browser) rotated this same token
        //   while this one was in flight. Nothing is issued or revoked and the cookie is NOT touched: deleting it here
        //   could erase the token the winning request just stored. The client may retry once with its current cookie.
        [AllowAnonymous]
        [CookieCsrfProtection]
        [EnableRateLimiting(RateLimitPolicies.Authentication)]
        [HttpPost("refresh")]
        [ProducesResponseType<WebSession>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
        {
            var refreshToken = RefreshTokenCookie.Read(Request);
            var outcome = refreshToken is null
                ? RefreshOutcome.Failed
                : await _authentication.RefreshSessionAsync(refreshToken, HttpContext.ToClientContext(), cancellationToken);

            switch (outcome.Status)
            {
                case RefreshStatus.Succeeded:
                    return Ok(StartSession(outcome.Session!));
                case RefreshStatus.LostConcurrencyRace:
                    return RefreshSupersededProblem();
                default:
                    RefreshTokenCookie.Delete(Response);
                    return AuthenticationFailedProblem();
            }
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

        private ObjectResult RefreshSupersededProblem()
        {
            var problem = ProblemDetailsFactory.CreateProblemDetails(
                HttpContext, StatusCodes.Status409Conflict, title: "The session was refreshed by a concurrent request.");
            problem.Extensions["code"] = RefreshSupersededCode;
            return new ObjectResult(problem) { StatusCode = StatusCodes.Status409Conflict };
        }
    }
}
