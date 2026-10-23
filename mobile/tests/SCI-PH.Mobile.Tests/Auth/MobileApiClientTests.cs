using System.Net;
using System.Net.Http.Headers;
using SCI_PH.Mobile.Common.Api;

namespace SCI_PH.Mobile.Tests.Auth;

// Traducción de respuestas y fallos de red a mensajes seguros; dirección del Mobile BFF por plataforma/compilación.
public class MobileApiClientTests
{
    [Theory]
    [InlineData(HttpStatusCode.BadRequest, MobileApiError.BadRequest, MobileApiErrorMessages.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized, MobileApiError.Unauthorized, MobileApiErrorMessages.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, MobileApiError.Forbidden, MobileApiErrorMessages.Forbidden)]
    [InlineData(HttpStatusCode.Conflict, MobileApiError.Conflict, MobileApiErrorMessages.Conflict)]
    [InlineData(HttpStatusCode.TooManyRequests, MobileApiError.TooManyRequests, MobileApiErrorMessages.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError, MobileApiError.Server, MobileApiErrorMessages.Server)]
    [InlineData(HttpStatusCode.ServiceUnavailable, MobileApiError.Server, MobileApiErrorMessages.Server)]
    public async Task ProblemDetailsBecomeSafeMessages(HttpStatusCode status, MobileApiError expected, string message)
    {
        var client = ClientReturning(() => FakeMobileBff.Problem(status, "Internal title (test)"));

        var ex = await Assert.ThrowsAsync<MobileApiException>(() => client.GetAsync<object>("api/mobile/test"));

        Assert.Equal(expected, ex.Error);
        Assert.Equal(status, ex.StatusCode);
        var shown = MobileApiErrorMessages.For(ex);
        Assert.Equal(message, shown);
        Assert.DoesNotContain("Internal", shown);
        Assert.DoesNotContain("Internal", ex.Message);
    }

    [Fact]
    public async Task TooManyRequestsHonoursRetryAfter()
    {
        var client = ClientReturning(() =>
        {
            var response = FakeMobileBff.Problem(HttpStatusCode.TooManyRequests, "Too many requests.");
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(42));
            return response;
        });

        var ex = await Assert.ThrowsAsync<MobileApiException>(() => client.GetAsync<object>("api/mobile/test"));

        Assert.Equal(TimeSpan.FromSeconds(42), ex.RetryAfter);
        Assert.Equal("Demasiados intentos. Espera 42 segundos e intenta nuevamente.", MobileApiErrorMessages.For(ex));
    }

    [Fact]
    public void ALongRetryAfterIsShownInMinutes()
    {
        var ex = new MobileApiException(MobileApiError.TooManyRequests, HttpStatusCode.TooManyRequests, TimeSpan.FromSeconds(90));

        Assert.Equal("Demasiados intentos. Espera 2 minutos e intenta nuevamente.", MobileApiErrorMessages.For(ex));
    }

    [Fact]
    public async Task ARefusedConnectionIsANetworkFailure()
    {
        var client = ClientReturning(() => throw new HttpRequestException("No such host is known. (sciph.test:5191)"));

        var ex = await Assert.ThrowsAsync<MobileApiException>(() => client.GetAsync<object>("api/mobile/test"));

        Assert.Equal(MobileApiError.Network, ex.Error);
        Assert.Equal(MobileApiErrorMessages.Network, MobileApiErrorMessages.For(ex, unauthorizedMessage: "credenciales"));
    }

    [Fact]
    public async Task ATimeoutIsReportedAsAConnectionFailure()
    {
        var handler = new DelegateHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://sciph.test/"), Timeout = TimeSpan.FromMilliseconds(50) };

        var ex = await Assert.ThrowsAsync<MobileApiException>(() => new MobileApiClient(http).GetAsync<object>("api/mobile/test"));

        Assert.Equal(MobileApiError.Timeout, ex.Error);
        Assert.Equal(MobileApiErrorMessages.Network, MobileApiErrorMessages.For(ex));
    }

    [Fact]
    public async Task ACallerCancellationIsNotDisguisedAsANetworkFailure()
    {
        using var cancellation = new CancellationTokenSource();
        var client = ClientReturning(() =>
        {
            cancellation.Cancel();
            cancellation.Token.ThrowIfCancellationRequested();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync<object>("api/mobile/test", cancellation.Token));
    }

    [Fact]
    public async Task AResponseThatIsNotTheExpectedJsonIsInvalid()
    {
        var client = ClientReturning(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>proxy</html>") });

        var ex = await Assert.ThrowsAsync<MobileApiException>(() => client.GetAsync<Dictionary<string, string>>("api/mobile/test"));

        Assert.Equal(MobileApiError.InvalidResponse, ex.Error);
        Assert.Equal(MobileApiErrorMessages.Server, MobileApiErrorMessages.For(ex));
    }

    [Fact]
    public async Task WithoutAConfiguredServerNothingIsSent()
    {
        var calls = 0;
        var handler = new DelegateHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        using var http = MobileApiClient.CreateHttpClient(new MobileApiOptions { BaseAddress = null });
        using var direct = new HttpClient(handler);

        var ex = await Assert.ThrowsAsync<MobileApiException>(() => new MobileApiClient(direct).GetAsync<object>("api/mobile/test"));

        Assert.Equal(MobileApiError.NotConfigured, ex.Error);
        Assert.Equal(0, calls);
        Assert.Null(http.BaseAddress);
        Assert.Equal(MobileApiErrorMessages.NotConfigured, MobileApiErrorMessages.For(ex));
    }

    [Theory]
    [InlineData(null, true, true, "http://10.0.2.2:5191/")]
    [InlineData(null, true, false, "http://localhost:5191/")]
    [InlineData(null, false, true, null)]
    [InlineData(null, false, false, null)]
    [InlineData("https://bff.example.test", false, true, "https://bff.example.test/")]
    [InlineData("https://bff.example.test", true, false, "https://bff.example.test/")]
    [InlineData("http://bff.example.test:5191", true, true, "http://bff.example.test:5191/")]
    [InlineData("http://bff.example.test:5191", false, true, null)]
    [InlineData("http://bff.example.test:5191", false, false, null)]
    [InlineData("not a url", true, true, null)]
    [InlineData("ftp://bff.example.test", false, false, null)]
    public void TheServerDependsOnBuildAndPlatformNeverOnAPersonalAddress(string? configured, bool isDebug, bool isAndroid, string? expected)
    {
        Assert.Equal(expected, MobileApiOptions.ResolveBaseAddress(configured, isDebug, isAndroid)?.AbsoluteUri);
    }

    [Fact]
    public void TheHttpClientUsesTheConfiguredServerAndTimeout()
    {
        using var http = MobileApiClient.CreateHttpClient(new MobileApiOptions
        {
            BaseAddress = new Uri("http://localhost:5191"),
            Timeout = TimeSpan.FromSeconds(7)
        });

        Assert.Equal("http://localhost:5191/", http.BaseAddress!.AbsoluteUri);
        Assert.Equal(TimeSpan.FromSeconds(7), http.Timeout);
    }

    private static MobileApiClient ClientReturning(Func<HttpResponseMessage> respond)
    {
        var http = new HttpClient(new DelegateHandler((_, _) => Task.FromResult(respond())))
        {
            BaseAddress = new Uri("http://sciph.test/")
        };
        return new MobileApiClient(http);
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
