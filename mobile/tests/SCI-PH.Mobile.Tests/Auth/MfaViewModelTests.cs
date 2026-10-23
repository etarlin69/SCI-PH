using System.Net;
using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Common.Authentication;
using SCI_PH.Mobile.Common.Authentication.Contracts;
using SCI_PH.Mobile.Features.Auth;
using SCI_PH.Mobile.Features.Auth.Services;
using SCI_PH.Mobile.Features.Auth.ViewModels;

namespace SCI_PH.Mobile.Tests.Auth;

public class MfaViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeAuthService _auth = new();
    private readonly PendingMfaChallenge _pendingMfa = new();
    private readonly FakeNavigator _navigator = new();
    private readonly FixedClock _clock = new(Now);
    private readonly MfaChallengeDto _challenge = new() { Token = "challenge-token", ExpiresAtUtc = Now.AddMinutes(5) };

    public MfaViewModelTests()
    {
        _pendingMfa.Set(_challenge);
    }

    private MfaViewModel Create() => new(_auth, _pendingMfa, _navigator, _clock);

    [Fact]
    public async Task AValidTotpIsSentAsCodeWithTheLoginChallenge()
    {
        var vm = Create();
        vm.Code = "123 456";

        await vm.VerifyCommand.ExecuteAsync(null);

        Assert.Equal(("challenge-token", "123456", (string?)null), Assert.Single(_auth.Verifications));
    }

    [Fact]
    public async Task ARecoveryCodeIsSentAsRecoveryCode()
    {
        var vm = Create();
        vm.ToggleRecoveryModeCommand.Execute(null);
        vm.RecoveryCode = " ABCD-EFGH ";

        await vm.VerifyCommand.ExecuteAsync(null);

        Assert.Equal(("challenge-token", (string?)null, "ABCD-EFGH"), Assert.Single(_auth.Verifications));
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("12a456")]
    [InlineData("1234567")]
    public async Task AnEmptyOrMalformedCodeIsBlocked(string code)
    {
        var vm = Create();
        vm.Code = code;

        await vm.VerifyCommand.ExecuteAsync(null);

        Assert.Empty(_auth.Verifications);
        Assert.Equal(AuthMessages.CodeRequired, vm.ErrorMessage);
    }

    [Fact]
    public async Task AnEmptyRecoveryCodeIsBlocked()
    {
        var vm = Create();
        vm.ToggleRecoveryModeCommand.Execute(null);
        vm.RecoveryCode = "  ";

        await vm.VerifyCommand.ExecuteAsync(null);

        Assert.Empty(_auth.Verifications);
        Assert.Equal(AuthMessages.RecoveryCodeRequired, vm.ErrorMessage);
    }

    [Fact]
    public async Task AnInvalidCodeShowsASafeErrorAndAllowsAnotherAttempt()
    {
        _auth.VerifyResult = () => throw new MobileApiException(MobileApiError.Unauthorized, HttpStatusCode.Unauthorized);
        var vm = Create();
        vm.Code = "000000";

        await vm.VerifyCommand.ExecuteAsync(null);

        Assert.Equal(AuthMessages.InvalidCode, vm.ErrorMessage);
        Assert.Empty(_navigator.Navigations);
        Assert.Same(_challenge, _pendingMfa.Current);
        Assert.False(vm.IsChallengeClosed);
        Assert.True(vm.VerifyCommand.CanExecute(null));
    }

    [Fact]
    public async Task SuccessCompletesTheSignInAndForgetsTheChallengeAndCode()
    {
        var vm = Create();
        vm.Code = "123456";

        await vm.VerifyCommand.ExecuteAsync(null);

        Assert.Equal(AuthRoutes.ClinicalArea, Assert.Single(_navigator.Navigations).Route);
        Assert.Null(_pendingMfa.Current);
        Assert.Equal(string.Empty, vm.Code);
        Assert.Equal(string.Empty, vm.RecoveryCode);
    }

    [Fact]
    public async Task AnExpiredChallengeIsNotSentAndRequiresANewLogin()
    {
        _clock.Now = _challenge.ExpiresAtUtc;
        var vm = Create();
        vm.Code = "123456";

        await vm.VerifyCommand.ExecuteAsync(null);

        Assert.Empty(_auth.Verifications);
        Assert.Equal(AuthMessages.MfaExpired, vm.ErrorMessage);
        Assert.True(vm.IsChallengeClosed);
        Assert.False(vm.VerifyCommand.CanExecute(null));
        Assert.Null(_pendingMfa.Current);
    }

    [Fact]
    public async Task AChallengeThatExpiresWhileVerifyingIsClosed()
    {
        _auth.VerifyResult = () =>
        {
            _clock.Now = _challenge.ExpiresAtUtc.AddSeconds(1);
            throw new MobileApiException(MobileApiError.Unauthorized, HttpStatusCode.Unauthorized);
        };
        var vm = Create();
        vm.Code = "123456";

        await vm.VerifyCommand.ExecuteAsync(null);

        Assert.Equal(AuthMessages.MfaExpired, vm.ErrorMessage);
        Assert.True(vm.IsChallengeClosed);
    }

    [Fact]
    public async Task WithoutTheEmergencyPermissionTheChallengeIsClosed()
    {
        _auth.VerifyResult = () => new ClinicalSignInResult(ClinicalSignInStatus.NoAccess);
        var vm = Create();
        vm.Code = "123456";

        await vm.VerifyCommand.ExecuteAsync(null);

        Assert.Equal(AuthMessages.NoEmergencyAccess, vm.ErrorMessage);
        Assert.True(vm.IsChallengeClosed);
        Assert.Empty(_navigator.Navigations);
    }

    [Fact]
    public async Task BackToLoginForgetsTheChallenge()
    {
        var vm = Create();
        vm.Code = "12";

        await vm.BackToLoginCommand.ExecuteAsync(null);

        Assert.Null(_pendingMfa.Current);
        Assert.Equal(string.Empty, vm.Code);
        Assert.Equal("..", Assert.Single(_navigator.Navigations).Route);
    }

    [Fact]
    public void LeavingTheMfaPageAnyOtherWayForgetsTheChallengeAndCodes()
    {
        var vm = Create();
        vm.Code = "123456";
        vm.RecoveryCode = "abcde-12345";

        vm.ForgetChallenge();

        Assert.Null(_pendingMfa.Current);
        Assert.Equal(string.Empty, vm.Code);
        Assert.Equal(string.Empty, vm.RecoveryCode);
        Assert.Empty(_navigator.Navigations);
    }
}
