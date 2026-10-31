using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SCI_PH.Infrastructure.ClinicalAi;

namespace SCI_PH.IntegrationTests.ClinicalAi
{
    // A simulated Azure OpenAI endpoint for the real provider's tests (ADR-014): the SDK's HTTP transport answers from a
    // script instead of the network, and every request the adapter really built is recorded. No Azure, no credentials, no
    // recorded real response: every answer here is synthetic.
    internal sealed class ScriptedAzureOpenAi : HttpMessageHandler
    {
        public const string Endpoint = "https://sciph-test.openai.azure.com/";
        public const string Deployment = "sciph-summary-dz-test";
        public const string ApiKey = "test-key-0123456789-not-a-secret";
        // Markers the provider "says" in errors and headers: they must never reach a log, an exception or a client.
        public const string ProviderMessage = "provider-message-marker-91f2";
        public const string RequestId = "request-id-marker-6c1e";

        private readonly ConcurrentQueue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _script = new();

        public ConcurrentQueue<Recorded> Requests { get; } = new();

        public sealed record Recorded(HttpMethod Method, Uri Uri, IReadOnlyDictionary<string, string> Headers, string Body);

        public ScriptedAzureOpenAi Then(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer)
        {
            _script.Enqueue(answer);
            return this;
        }

        public ScriptedAzureOpenAi Then(Func<HttpResponseMessage> answer) => Then((_, _) => Task.FromResult(answer()));

        public ScriptedAzureOpenAi ThenNetworkError() => Then((_, _) => throw new HttpRequestException("connection reset"));

        // Waits until the caller cancels (a provider that never answers).
        public ScriptedAzureOpenAi ThenNever(Action? cancelled = null) => Then(async (_, ct) =>
        {
            try
            {
                await Task.Delay(System.Threading.Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
                cancelled?.Invoke();
                throw;
            }

            throw new InvalidOperationException();
        });

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Enqueue(new Recorded(
                request.Method, request.RequestUri!,
                request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase), body));
            if (!_script.TryDequeue(out var answer))
            {
                throw new InvalidOperationException("The provider received a request the test did not expect.");
            }

            return await answer(request, cancellationToken);
        }

        public AzureOpenAiClinicalAiService Provider(
            Action<ClinicalAiOptions>? configure = null, AuthenticationTokenProvider? credential = null, ILogger<AzureOpenAiClinicalAiService>? logger = null)
        {
            var options = Options();
            configure?.Invoke(options);
            return new AzureOpenAiClinicalAiService(
                Microsoft.Extensions.Options.Options.Create(options), TimeProvider.System, logger ?? NullLogger<AzureOpenAiClinicalAiService>.Instance,
                new HttpClientPipelineTransport(new HttpClient(this, disposeHandler: false)), credential);
        }

        public static ClinicalAiOptions Options() => new()
        {
            Provider = ClinicalAiOptions.AzureOpenAiProvider,
            AllowExternalClinicalData = true,
            AzureOpenAI = new AzureOpenAiClinicalAiOptions
            {
                Endpoint = Endpoint,
                Deployment = Deployment,
                AuthenticationMode = AzureOpenAiAuthenticationMode.ApiKey,
                ApiKey = ApiKey
            }
        };

        // The settings of a Web BFF host with this provider (development: API key).
        public static Dictionary<string, string?> Settings() => new()
        {
            ["ClinicalAi:Provider"] = ClinicalAiOptions.AzureOpenAiProvider,
            ["ClinicalAi:AllowExternalClinicalData"] = "true",
            ["ClinicalAi:AzureOpenAI:Endpoint"] = Endpoint,
            ["ClinicalAi:AzureOpenAI:Deployment"] = Deployment,
            ["ClinicalAi:AzureOpenAI:AuthenticationMode"] = "ApiKey",
            ["ClinicalAi:AzureOpenAI:ApiKey"] = ApiKey
        };

        // A chat completion as the v1 API answers it.
        public static HttpResponseMessage Completion(
            string? content, string finishReason = "stop", string? refusal = null, int inputTokens = 1500, int outputTokens = 300, int reasoningTokens = 0)
        {
            var body = JsonSerializer.Serialize(new
            {
                id = "chatcmpl-test",
                @object = "chat.completion",
                created = 1_790_000_000,
                model = AzureOpenAiClinicalAiOptions.DefaultModelVersion,
                choices = new[] { new { index = 0, finish_reason = finishReason, message = new { role = "assistant", content, refusal } } },
                usage = new
                {
                    prompt_tokens = inputTokens,
                    completion_tokens = outputTokens,
                    total_tokens = inputTokens + outputTokens,
                    completion_tokens_details = new { reasoning_tokens = reasoningTokens }
                }
            });
            return Response(HttpStatusCode.OK, body);
        }

        // An error as Azure OpenAI answers it, with a provider message and request id that must not travel further.
        public static HttpResponseMessage Error(HttpStatusCode status, string code, string? innerCode = null)
        {
            var body = JsonSerializer.Serialize(new
            {
                error = new
                {
                    code,
                    message = $"{ProviderMessage} (deployment {Deployment})",
                    innererror = innerCode is null ? null : new { code = innerCode, content_filter_result = new { self_harm = new { filtered = true, severity = "high" } } }
                }
            });
            return Response(status, body);
        }

        private static HttpResponseMessage Response(HttpStatusCode status, string body)
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            response.Headers.Add("apim-request-id", RequestId);
            response.Headers.Add("x-request-id", RequestId);
            return response;
        }
    }
}
