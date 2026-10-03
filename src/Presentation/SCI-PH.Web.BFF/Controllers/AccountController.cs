using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.Application.Features.Accounts;
using SCI_PH.Infrastructure.Web;
using SCI_PH.Web.BFF.Security;

namespace SCI_PH.Web.BFF.Controllers
{
    // Public account flows reached from email links. Responses never reveal whether an email has an account.
    [ApiController]
    [Route("api/web/auth")]
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class AccountController : ControllerBase
    {
        private const string GenericAccepted = "If the address belongs to an account, an email has been sent.";
        private const string InvalidLink = "The link is invalid or has expired.";

        private readonly AccountInvitationService _invitations;
        private readonly AccountRecoveryService _recovery;

        public AccountController(AccountInvitationService invitations, AccountRecoveryService recovery)
        {
            _invitations = invitations;
            _recovery = recovery;
        }

        // Only the invitation token and the new password: subject, email and role come from the invitation.
        [EnableRateLimiting(RateLimitPolicies.Authentication)]
        [HttpPost("activate-account")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> ActivateAccount(ActivateAccountRequest request, CancellationToken cancellationToken)
        {
            var result = await _invitations.ActivateAsync(request, HttpContext.ToClientContext(), cancellationToken);
            if (result.Succeeded)
            {
                return NoContent();
            }

            if (result.Errors.Count == 0)
            {
                return Problem(statusCode: StatusCodes.Status400BadRequest, title: InvalidLink);
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(nameof(request.Password), error);
            }

            return ValidationProblem(ModelState);
        }

        [EnableRateLimiting(RateLimitPolicies.AccountRecovery)]
        [HttpPost("forgot-password")]
        [ProducesResponseType(StatusCodes.Status202Accepted)]
        public async Task<IActionResult> ForgotPassword(EmailRequest request, CancellationToken cancellationToken)
        {
            await _recovery.RequestPasswordResetAsync(request.Email, HttpContext.ToClientContext(), cancellationToken);
            return Accepted(new { message = GenericAccepted });
        }

        [EnableRateLimiting(RateLimitPolicies.AccountRecovery)]
        [HttpPost("reset-password")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken)
        {
            var result = await _recovery.ResetPasswordAsync(request, HttpContext.ToClientContext(), cancellationToken);
            if (result.Succeeded)
            {
                return NoContent();
            }

            if (result.FailureReason != SecurityFailureReason.PasswordPolicy)
            {
                return Problem(statusCode: StatusCodes.Status400BadRequest, title: InvalidLink);
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(nameof(request.NewPassword), error);
            }

            return ValidationProblem(ModelState);
        }

        [EnableRateLimiting(RateLimitPolicies.AccountRecovery)]
        [HttpPost("resend-confirmation")]
        [ProducesResponseType(StatusCodes.Status202Accepted)]
        public async Task<IActionResult> ResendConfirmation(EmailRequest request, CancellationToken cancellationToken)
        {
            await _recovery.RequestEmailConfirmationAsync(request.Email, HttpContext.ToClientContext(), cancellationToken);
            return Accepted(new { message = GenericAccepted });
        }

        [EnableRateLimiting(RateLimitPolicies.AccountRecovery)]
        [HttpPost("confirm-email")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> ConfirmEmail(ConfirmEmailRequest request, CancellationToken cancellationToken) =>
            await _recovery.ConfirmEmailAsync(request, HttpContext.ToClientContext(), cancellationToken)
                ? NoContent()
                : Problem(statusCode: StatusCodes.Status400BadRequest, title: InvalidLink);
    }
}
