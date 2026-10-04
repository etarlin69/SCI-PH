using Microsoft.AspNetCore.Mvc;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.Application.Features.Authentication;
using SCI_PH.Web.BFF.Security;

namespace SCI_PH.Web.BFF.Controllers
{
    // Authenticated MFA management. Shared keys and recovery codes appear only in these responses.
    // Enabling or disabling MFA ends every session, including the current one.
    [ApiController]
    [Route("api/web/auth/mfa")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class MfaController : ControllerBase
    {
        private const string InvalidCode = "The authenticator code is invalid.";

        private readonly MfaManagementService _mfa;
        private readonly ICurrentUser _currentUser;

        public MfaController(MfaManagementService mfa, ICurrentUser currentUser)
        {
            _mfa = mfa;
            _currentUser = currentUser;
        }

        private Guid UserId => _currentUser.UserId!.Value;

        [HttpPost("setup")]
        [ProducesResponseType<MfaSetup>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Setup(CancellationToken cancellationToken)
        {
            var setup = await _mfa.BeginSetupAsync(UserId, HttpContext.ToClientContext(), cancellationToken);
            return setup is null
                ? Problem(statusCode: StatusCodes.Status409Conflict, title: "MFA is already enabled.")
                : Ok(setup);
        }

        [HttpPost("enable")]
        [ProducesResponseType<RecoveryCodes>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Enable(MfaCodeRequest request, CancellationToken cancellationToken)
        {
            var codes = await _mfa.EnableAsync(UserId, request.Code, HttpContext.ToClientContext(), cancellationToken);
            if (codes is null)
            {
                return InvalidCodeProblem();
            }

            RefreshTokenCookie.Delete(Response);
            return Ok(codes);
        }

        [HttpPost("disable")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Disable(MfaCodeRequest request, CancellationToken cancellationToken)
        {
            if (!await _mfa.DisableAsync(UserId, request.Code, HttpContext.ToClientContext(), cancellationToken))
            {
                return InvalidCodeProblem();
            }

            RefreshTokenCookie.Delete(Response);
            return NoContent();
        }

        [HttpPost("recovery-codes/regenerate")]
        [ProducesResponseType<RecoveryCodes>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> RegenerateRecoveryCodes(MfaCodeRequest request, CancellationToken cancellationToken)
        {
            var codes = await _mfa.RegenerateRecoveryCodesAsync(UserId, request.Code, HttpContext.ToClientContext(), cancellationToken);
            return codes is null ? InvalidCodeProblem() : Ok(codes);
        }

        private ObjectResult InvalidCodeProblem() => Problem(statusCode: StatusCodes.Status400BadRequest, title: InvalidCode);
    }
}
