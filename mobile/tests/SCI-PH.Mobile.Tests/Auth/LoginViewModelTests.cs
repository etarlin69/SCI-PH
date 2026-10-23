using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Common.Authentication;
using SCI_PH.Mobile.Common.Authentication.Contracts;
using SCI_PH.Mobile.Features.Auth;
using SCI_PH.Mobile.Features.Auth.Services;
using SCI_PH.Mobile.Features.Auth.ViewModels;

namespace SCI_PH.Mobile.Tests.Auth;

public class LoginViewModelTests
{
    private readonly FakeAuthService _auth = new();
    private readonly PendingMfaChallenge _pendingMfa = new();
    private readonly FakeNavigator _navigator = new();

    private LoginViewModel Create(string identifier = FakeMobileBff.Identifier, string password = FakeMobileBff.Password) =>
        new(_auth, _pendingMfa, _navigator) { Identifier = identifier, Password = password };

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnEmptyIdentifierIsBlocked(string identifier)
    {
        var vm = Create(identifier: identifier);

        await vm.LoginCommand.ExecuteAsync(null);

        Assert.Empty(_auth.Logins);
        Assert.Equal(AuthMessages.IdentifierRequired, vm.ErrorMessage);
        Assert.Empty(_navigator.Navigations);
    }

    [Fact]
    public async Task AnEmptyPasswordIsBlocked()
    {
        var vm = Create(password: "");

        await vm.LoginCommand.ExecuteAsync(null);

        Assert.Empty(_auth.Logins);
        Assert.Equal(AuthMessages.PasswordRequired, vm.ErrorMessage);
    }

    [Fact]
    public async Task AValidSubmitCallsTheAuthServiceAndForgetsThePassword()
    {
        var vm = Create();

        await vm.LoginCommand.ExecuteAsync(null);

        Assert.Equal((FakeMobileBff.Identifier, FakeMobileBff.Password), Assert.Single(_auth.Logins));
        Assert.Equal(string.Empty, vm.Password);
    }

    [Fact]
    public async Task ASuccessfulLoginWithoutMfaOpensTheClinicalArea()
    {
        var vm = Create();

        await vm.LoginCommand.ExecuteAsync(null);

        Assert.Equal(AuthRoutes.ClinicalArea, Assert.Single(_navigator.Navigations).Route);
        Assert.False(vm.HasError);
        Assert.Null(_pendingMfa.Current);
    }

    [Fact]
    public async Task ALoginThatRequiresMfaKeepsOnlyTheChallengeAndOpensMfa()
    {
        var challenge = new MfaChallengeDto { Token = "challenge", ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(5) };
        _auth.LoginResult = () => new ClinicalSignInResult(ClinicalSignInStatus.MfaRequired, challenge);
        var vm = Create();

        await vm.LoginCommand.ExecuteAsync(null);

        Assert.Same(challenge, _pendingMfa.Current);
        Assert.Equal(AuthRoutes.Mfa, Assert.Single(_navigator.Navigations).Route);
        Assert.Equal(string.Empty, vm.Password);
    }

    [Fact]
    public async Task InvalidCredentialsShowASafeError()
    {
        _auth.LoginResult = () => throw new MobileApiException(MobileApiError.Unauthorized, System.Net.HttpStatusCode.Unauthorized);
        var vm = Create();

        await vm.LoginCommand.ExecuteAsync(null);

        Assert.Equal(AuthMessages.SignInFailed, vm.ErrorMessage);
        Assert.Empty(_navigator.Navigations);
        Assert.Equal(string.Empty, vm.Password);
        Assert.False(vm.IsBusy);
    }

    [Theory]
    [InlineData(MobileApiError.Network)]
    [InlineData(MobileApiError.Timeout)]
    public async Task ANetworkFailureShowsTheConnectionMessageNotAnInvalidCredentialsOne(MobileApiError error)
    {
        _auth.LoginResult = () => throw new MobileApiException(error, innerException: new HttpRequestException("secret detail"));
        var vm = Create();

        await vm.LoginCommand.ExecuteAsync(null);

        Assert.Equal(MobileApiErrorMessages.Network, vm.ErrorMessage);
        Assert.NotEqual(AuthMessages.SignInFailed, vm.ErrorMessage);
    }

    [Fact]
    public async Task AnAccountWithoutTheEmergencyPermissionCannotEnter()
    {
        _auth.LoginResult = () => new ClinicalSignInResult(ClinicalSignInStatus.NoAccess);
        var vm = Create();

        await vm.LoginCommand.ExecuteAsync(null);

        Assert.Equal(AuthMessages.NoEmergencyAccess, vm.ErrorMessage);
        Assert.Empty(_navigator.Navigations);
    }

    [Fact]
    public async Task AnUnexpectedErrorNeverShowsTheExceptionText()
    {
        _auth.LoginResult = () => throw new InvalidOperationException("stack trace and internals");
        var vm = Create();

        await vm.LoginCommand.ExecuteAsync(null);

        Assert.Equal(LoginViewModel.GenericErrorMessage, vm.ErrorMessage);
    }

    [Fact]
    public async Task ADoubleSubmitReachesTheServerOnce()
    {
        _auth.LoginGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = Create();

        var first = vm.LoginCommand.ExecuteAsync(null);
        Assert.True(vm.IsBusy);
        Assert.False(vm.LoginCommand.CanExecute(null));

        vm.Password = FakeMobileBff.Password;
        await vm.LoginCommand.ExecuteAsync(null);

        _auth.LoginGate.SetResult();
        await first;

        Assert.Single(_auth.Logins);
        Assert.Single(_navigator.Navigations);
        Assert.True(vm.LoginCommand.CanExecute(null));
    }

    [Fact]
    public async Task BackReturnsToTheEntryAndClearsThePassword()
    {
        var vm = Create();

        await vm.BackCommand.ExecuteAsync(null);

        Assert.Equal("..", Assert.Single(_navigator.Navigations).Route);
        Assert.Equal(string.Empty, vm.Password);
    }
}
