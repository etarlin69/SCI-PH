using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.Application.Features.Authentication;
using SCI_PH.Infrastructure.Web;

namespace SCI_PH.Mobile.BFF.Controllers
{
    public sealed record MobileLoginResponse(bool MfaRequired, AuthenticationResult? Session, MfaChallenge? MfaChallenge);

    // Transport only: all rules live in AuthenticationService (shared with the Web BFF).
    // Mobile receives the refresh token in the body (never a cookie) and keeps it in MAUI SecureStorage.
    [ApiController]
    [Route("api/mobile/auth")]
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

        private ClientContext Client =>
            new(HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString());

        [AllowAnonymous]
        [EnableRateLimiting(RateLimitPolicies.Authentication)]
        [HttpPost("login")]
        [ProducesResponseType<MobileLoginResponse>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
        {
            var result = await _authentication.LoginAsync(request, Client, cancellationToken);
            return result is null
                ? AuthenticationFailedProblem()
                : Ok(new MobileLoginResponse(result.RequiresMfa, result.Session, result.Challenge));
        }

        [AllowAnonymous]
        [EnableRateLimiting(RateLimitPolicies.Authentication)]
        [HttpPost("mfa/verify")]
        [ProducesResponseType<AuthenticationResult>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> VerifyMfa(MfaVerifyRequest request, CancellationToken cancellationToken)
        {
            var session = await _authentication.VerifyMfaAsync(request, Client, cancellationToken);
            return session is null ? AuthenticationFailedProblem() : Ok(session);
        }

        [AllowAnonymous]
        [EnableRateLimiting(RateLimitPolicies.Authentication)]
        [HttpPost("refresh")]
        [ProducesResponseType<AuthenticationResult>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> Refresh(RefreshTokenRequest request, CancellationToken cancellationToken)
        {
            var result = await _authentication.RefreshAsync(request.RefreshToken, Client, cancellationToken);
            return result is null ? AuthenticationFailedProblem() : Ok(result);
        }

        // Holding the refresh token is enough to end its session, even after the access token expired.
        [AllowAnonymous]
        [HttpPost("logout")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> Logout(RefreshTokenRequest request, CancellationToken cancellationToken)
        {
            await _authentication.LogoutAsync(request.RefreshToken, Client, cancellationToken);
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
            var result = await _authentication.ChangePasswordAsync(_currentUser.UserId!.Value, request, Client, cancellationToken);
            if (result.Succeeded)
            {
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
            await _authentication.RevokeAllSessionsAsync(_currentUser.UserId!.Value, Client, cancellationToken);
            return NoContent();
        }

        private ObjectResult AuthenticationFailedProblem() => Problem(statusCode: StatusCodes.Status401Unauthorized, title: AuthenticationFailed);
    }
}
