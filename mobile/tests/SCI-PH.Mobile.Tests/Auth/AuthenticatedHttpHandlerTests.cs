using System.Net;
using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Common.Authentication;

namespace SCI_PH.Mobile.Tests.Auth;

// Handler real + MobileAuthService real contra el BFF falso: Bearer, 401 → un refresh → un reintento, y single-flight.
public class AuthenticatedHttpHandlerTests
{
    [Fact]
    public async Task TheBearerTokenIsAddedWithASession()
    {
        using var h = new AuthHarness();
        await h.SignInAsync();

        await h.GetProtectedAsync();

        var request = Assert.Single(h.Bff.ProtectedRequests);
        Assert.Equal($"Bearer {h.Session.AccessToken}", Assert.Single(request.Authorization));
    }

    [Fact]
    public async Task WithoutATokenNoAuthorizationHeaderIsSent()
    {
        using var h = new AuthHarness();

        var ex = await Assert.ThrowsAsync<MobileApiException>(h.GetProtectedAsync);

        Assert.Equal(MobileApiError.Unauthorized, ex.Error);
        Assert.Empty(Assert.Single(h.Bff.ProtectedRequests).Authorization);
        Assert.Equal(0, h.Bff.RefreshCalls);
    }

    [Fact]
    public async Task AnExpiredAccessTokenTriggersOneRefreshAndOneRetry()
    {
        using var h = new AuthHarness();
        await h.SignInAsync();
        var oldRefreshToken = Assert.Single(h.Storage.Values).Value;
        h.Bff.ExpireAccessToken();

        var response = await h.GetProtectedAsync();

        Assert.True(response.Ok);
        Assert.Equal(1, h.Bff.RefreshCalls);
        Assert.Equal(2, h.Bff.ProtectedCalls);
        Assert.Equal(h.Bff.AccessToken, h.Session.AccessToken);

        // Rotación: SecureStorage tiene el refresh nuevo, no el viejo.
        var stored = Assert.Single(h.Storage.Values).Value;
        Assert.Equal(h.Bff.RefreshToken, stored);
        Assert.NotEqual(oldRefreshToken, stored);
        Assert.True(h.Session.IsAuthenticated);
    }

    [Fact]
    public async Task ARetriedPostKeepsItsBodyAndCarriesOneAuthorizationAndOneContentType()
    {
        using var h = new AuthHarness();
        await h.SignInAsync();
        h.Bff.ExpireAccessToken();

        await h.Protected.PostAsync<object, AuthHarness.SignedIn>(
            FakeMobileBff.ProtectedPath.TrimStart('/'), new { clientRequestId = "5d1c0c55-0000-4000-8000-000000000001" });

        Assert.Equal(2, h.Bff.ProtectedRequests.Count);
        var (first, retry) = (h.Bff.ProtectedRequests[0], h.Bff.ProtectedRequests[1]);
        Assert.Equal("POST", retry.Method);
        Assert.Equal(first.Body, retry.Body);
        Assert.Contains("5d1c0c55-0000-4000-8000-000000000001", retry.Body);
        Assert.Equal($"Bearer {h.Session.AccessToken}", Assert.Single(retry.Authorization));
        Assert.NotEqual(first.Authorization, retry.Authorization);
        Assert.StartsWith("application/json", Assert.Single(retry.ContentType));
    }

    [Fact]
    public async Task ASecond401EndsTheSessionInsteadOfRefreshingAgain()
    {
        using var h = new AuthHarness();
        await h.SignInAsync();
        h.Bff.RejectAllAccessTokens = true;

        var ex = await Assert.ThrowsAsync<MobileApiException>(h.GetProtectedAsync);

        Assert.Equal(MobileApiError.Unauthorized, ex.Error);
        Assert.Equal(1, h.Bff.RefreshCalls);
        Assert.Equal(2, h.Bff.ProtectedCalls);
        Assert.False(h.Session.IsAuthenticated);
        Assert.Empty(h.Storage.Values);
        Assert.Equal(1, h.SessionExpiredCount);
    }

    [Fact]
    public async Task ARejectedRefreshClearsTheSessionAndTheStoredToken()
    {
        using var h = new AuthHarness();
        await h.SignInAsync();
        h.Bff.RevokeSession();

        var ex = await Assert.ThrowsAsync<MobileApiException>(h.GetProtectedAsync);

        Assert.Equal(MobileApiError.Unauthorized, ex.Error);
        Assert.Equal(1, h.Bff.RefreshCalls);
        Assert.Equal(1, h.Bff.ProtectedCalls);
        Assert.Null(h.Session.AccessToken);
        Assert.Null(h.Session.CurrentUser);
        Assert.Empty(h.Storage.Values);
        Assert.Equal(1, h.SessionExpiredCount);
    }

    [Fact]
    public async Task AnUnreachableRefreshKeepsTheStoredTokenForLater()
    {
        using var h = new AuthHarness();
        await h.SignInAsync();
        var refreshToken = Assert.Single(h.Storage.Values).Value;
        h.Bff.ExpireAccessToken();
        h.Bff.UnreachablePaths.Add("/api/mobile/auth/refresh");

        var ex = await Assert.ThrowsAsync<MobileApiException>(h.GetProtectedAsync);

        Assert.Equal(MobileApiError.Network, ex.Error);
        Assert.Equal(refreshToken, Assert.Single(h.Storage.Values).Value);
        Assert.Equal(0, h.SessionExpiredCount);
    }

    [Fact]
    public async Task ConcurrentUnauthorizedRequestsShareASingleRefresh()
    {
        const int requests = 4;
        using var h = new AuthHarness();
        await h.SignInAsync();
        h.Bff.ExpireAccessToken();

        // Todas reciben su 401 con el token viejo antes de que empiece ningún refresh, y el refresh queda retenido
        // hasta que el test lo libera: así las cuatro compiten de verdad por renovar.
        h.Bff.HoldStaleRequestsUntil(requests);
        h.Bff.RefreshGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var calls = Enumerable.Range(0, requests).Select(_ => Task.Run(h.GetProtectedAsync)).ToArray();
        while (h.Bff.RefreshCalls == 0)
        {
            await Task.Yield();
        }

        h.Bff.RefreshGate.SetResult();
        var responses = await Task.WhenAll(calls);

        Assert.All(responses, response => Assert.True(response.Ok));
        Assert.Equal(1, h.Bff.RefreshCalls);
        Assert.Equal(requests * 2, h.Bff.ProtectedCalls);
        Assert.True(h.Session.IsAuthenticated);
        Assert.Equal(h.Bff.RefreshToken, Assert.Single(h.Storage.Values).Value);
    }

    [Fact]
    public async Task ARefreshRequestedWithAnAlreadyReplacedTokenReusesTheNewOne()
    {
        using var h = new AuthHarness();
        await h.SignInAsync();
        var stale = h.Session.AccessToken!;
        h.Bff.ExpireAccessToken();

        Assert.True(await h.Auth.TryRefreshAsync(stale, CancellationToken.None));
        Assert.True(await h.Auth.TryRefreshAsync(stale, CancellationToken.None));

        Assert.Equal(1, h.Bff.RefreshCalls);
    }

    [Fact]
    public async Task ARequestThatSucceedsIsNotRetried()
    {
        using var h = new AuthHarness();
        await h.SignInAsync();

        await h.GetProtectedAsync();
        await h.GetProtectedAsync();

        Assert.Equal(2, h.Bff.ProtectedCalls);
        Assert.Equal(0, h.Bff.RefreshCalls);
    }

    [Fact]
    public async Task ThePublicClientNeverCarriesTheBearerToken()
    {
        using var h = new AuthHarness();
        await h.SignInAsync();
        var inspector = new RecordingHandler();
        using var publicHttp = new HttpClient(inspector) { BaseAddress = new Uri("http://sciph.test/") };
        var api = new MobileAuthApi(new MobileApiClient(publicHttp));

        await Assert.ThrowsAsync<MobileApiException>(() => api.RefreshAsync("refresh", CancellationToken.None));

        Assert.Null(Assert.Single(inspector.Requests).Headers.Authorization);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        }
    }
}
