using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using SCI_PH.Application.Abstractions.AI;

namespace SCI_PH.Infrastructure.ClinicalAi
{
    // The real clinical AI provider (ADR-014): an Azure OpenAI (Microsoft Foundry) deployment of the summary model, through
    // the official OpenAI .NET SDK against the resource's "openai/v1/" API. Application only sees IClinicalAiService.
    // - Chat Completions, stateless: each call is independent, with no stored completion (store: false), no conversation,
    //   no tools. It sends ONLY the fixed system message and the minimized input of EncounterSummaryPromptV1, nothing else.
    // - Strict structured output (EncounterSummaryPromptV1.ResponseSchema); reasoning effort none; max_completion_tokens
    //   bounds the answer, reasoning included. No temperature, top_p or max_tokens: reasoning models reject them.
    // - The answer is parsed by EncounterSummaryPromptV1.ParseDraft and still validated whole by Application: the schema
    //   never replaces the validator. A refusal or a filtered answer is never parsed.
    // - Every failure becomes a ClinicalAiProviderException/ClinicalAiOutputException with a category; none carries the
    //   provider's message, body, headers, request id, the deployment, the prompt or any clinical content.
    // - Retries: the SDK's are off. One retry only for a network error, a 408 or a 5xx, and only while less than half of
    //   the timeout has passed; never for authentication, configuration, filter, refusal, the provider's 429 or a timeout.
    // - Nothing is called at startup: building the client contacts no one; availability is known per request.
    public sealed class AzureOpenAiClinicalAiService : IClinicalAiService
    {
        public const string ProviderName = "AzureOpenAI";

        // The rendered user message (the minimized input is about 60 000 characters at most); above this nothing is sent.
        public const int MaxUserMessageCharacters = 80_000;

        // The SDK's own network timeout stays above the operation's, so the operation's cancellation always decides.
        private static readonly TimeSpan _networkTimeoutMargin = TimeSpan.FromSeconds(5);

        private static readonly BinaryData _schema = BinaryData.FromString(EncounterSummaryPromptV1.ResponseSchema);

        private readonly ChatClient _chat;
        private readonly ClinicalAiOptions _options;
        private readonly TimeProvider _time;
        private readonly ILogger<AzureOpenAiClinicalAiService> _logger;

        public AzureOpenAiClinicalAiService(IOptions<ClinicalAiOptions> options, TimeProvider time, ILogger<AzureOpenAiClinicalAiService> logger)
            : this(options, time, logger, transport: null, credential: null)
        {
        }

        // Tests: a simulated HTTP transport (no network) and a token credential instead of the managed identity.
        internal AzureOpenAiClinicalAiService(
            IOptions<ClinicalAiOptions> options,
            TimeProvider time,
            ILogger<AzureOpenAiClinicalAiService> logger,
            PipelineTransport? transport,
            AuthenticationTokenProvider? credential)
        {
            _options = options.Value;
            _time = time;
            _logger = logger;
            var azure = _options.AzureOpenAI;
            var client = new OpenAIClientOptions
            {
                Endpoint = new Uri(new Uri(azure.Endpoint!), "openai/v1/"),
                RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
                NetworkTimeout = Timeout + _networkTimeoutMargin
            };
            if (transport is not null)
            {
                client.Transport = transport;
            }

            // OPENAI001: the SDK marks the Entra ID (BearerTokenPolicy) constructor as evaluation; it is the documented way
            // to authenticate to the Azure OpenAI v1 API with Microsoft Entra ID.
#pragma warning disable OPENAI001
            _chat = azure.AuthenticationMode == AzureOpenAiAuthenticationMode.ApiKey
                ? new ChatClient(azure.Deployment, new ApiKeyCredential(azure.ApiKey!), client)
                : new ChatClient(azure.Deployment, new BearerTokenPolicy(credential ?? CreateCredential(azure), azure.TokenScope), client);
#pragma warning restore OPENAI001
        }

        public string Provider => ProviderName;

        public string ModelVersion => _options.AzureOpenAI.ModelVersion;

        public string PromptVersion => EncounterSummaryPromptV1.Version;

        public TimeSpan Timeout => TimeSpan.FromSeconds(_options.TimeoutSeconds);

        public bool SendsDataExternally => true;

        // Microsoft Entra ID: the managed identity in Production; the developer's sign-in in development. Created without
        // contacting anyone (a token is requested with the first call).
        internal static AuthenticationTokenProvider CreateCredential(AzureOpenAiClinicalAiOptions azure) => azure.AuthenticationMode switch
        {
            AzureOpenAiAuthenticationMode.ManagedIdentity => new ManagedIdentityCredential(string.IsNullOrWhiteSpace(azure.ManagedIdentityClientId)
                ? ManagedIdentityId.SystemAssigned
                : ManagedIdentityId.FromUserAssignedClientId(azure.ManagedIdentityClientId)),
            AzureOpenAiAuthenticationMode.DefaultAzureCredential => new DefaultAzureCredential(),
            var other => throw new InvalidOperationException($"No Entra ID credential for authentication mode {other}.")
        };

        public async Task<EncounterSummaryDraft> SummarizeEncounterAsync(EncounterSummaryInput input, CancellationToken cancellationToken)
        {
            var prompt = EncounterSummaryPromptV1.Render(input);
            if (prompt.User.Length > MaxUserMessageCharacters)
            {
                throw new ClinicalAiProviderException(
                    ClinicalAiFailure.ConfigurationError, "The input exceeds the provider request limit.", beforeTransmission: true);
            }

            ChatMessage[] messages = [new SystemChatMessage(prompt.System), new UserChatMessage(prompt.User)];
            var started = _time.GetTimestamp();
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    ClientResult<ChatCompletion> result = await _chat.CompleteChatAsync(messages, Request(), cancellationToken);
                    return Read(result.Value);
                }
                catch (ClientResultException exception) when (attempt == 1 && IsTransient(exception.Status) && _time.GetElapsedTime(started) < Timeout / 2)
                {
                    _logger.LogWarning("Clinical AI provider call failed transiently (status {Status}); retrying once.", exception.Status);
                }
                catch (ClientResultException exception)
                {
                    var failure = Categorize(exception);
                    _logger.LogWarning("Clinical AI provider call failed (status {Status}, {Failure}).", exception.Status, failure);
                    // The SDK exception (its message holds the provider's answer) is never attached.
                    throw new ClinicalAiProviderException(failure, $"The provider answered status {exception.Status}.");
                }
                catch (AuthenticationFailedException)
                {
                    // No token: the request was never sent.
                    _logger.LogWarning("Clinical AI provider credential failed ({Failure}).", ClinicalAiFailure.AuthenticationFailed);
                    throw new ClinicalAiProviderException(
                        ClinicalAiFailure.AuthenticationFailed, "No Microsoft Entra ID token for the provider.", beforeTransmission: true);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // The SDK's own network timeout (the operation's timeout normally cancels first).
                    throw new ClinicalAiProviderException(ClinicalAiFailure.Timeout, "The provider did not answer in time.");
                }
            }
        }

        // A new options object per call: the SDK fills it with the request (messages, model) when sending. OPENAI001: the SDK
        // marks reasoning_effort as evaluation; it is the documented parameter of reasoning models.
#pragma warning disable OPENAI001
        private ChatCompletionOptions Request() => new()
        {
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                EncounterSummaryPromptV1.SchemaName, _schema, jsonSchemaFormatDescription: null, jsonSchemaIsStrict: true),
            MaxOutputTokenCount = _options.AzureOpenAI.MaxCompletionTokens,
            ReasoningEffortLevel = ChatReasoningEffortLevel.None,
            StoredOutputEnabled = false
        };
#pragma warning restore OPENAI001

        private EncounterSummaryDraft Read(ChatCompletion completion)
        {
            var usage = completion.Usage is { } tokens
                ? new ClinicalAiUsage(tokens.InputTokenCount, tokens.OutputTokenCount, tokens.OutputTokenDetails?.ReasoningTokenCount)
                : null;
            if (!string.IsNullOrEmpty(completion.Refusal))
            {
                throw new ClinicalAiProviderException(ClinicalAiFailure.ContentFiltered, "The model refused to answer.", usage: usage);
            }

            if (completion.FinishReason == ChatFinishReason.ContentFilter)
            {
                throw new ClinicalAiProviderException(ClinicalAiFailure.ContentFiltered, "The answer was filtered.", usage: usage);
            }

            if (completion.FinishReason == ChatFinishReason.Length)
            {
                throw new ClinicalAiOutputException("The answer was cut at the output limit.", usage: usage);
            }

            if (completion.FinishReason != ChatFinishReason.Stop)
            {
                throw new ClinicalAiOutputException("The answer did not finish as a document.", usage: usage);
            }

            var text = string.Concat(completion.Content.Where(p => p.Kind == ChatMessageContentPartKind.Text).Select(p => p.Text));
            try
            {
                return EncounterSummaryPromptV1.ParseDraft(text, ModelVersion) with { Usage = usage };
            }
            catch (ClinicalAiOutputException exception)
            {
                throw new ClinicalAiOutputException(exception.Message, usage: usage);
            }
        }

        // 0: no response (network); 408 and 5xx: the provider may answer the same request next time.
        private static bool IsTransient(int status) => status is 0 or 408 or >= 500;

        private static ClinicalAiFailure Categorize(ClientResultException exception)
        {
            var code = ErrorCode(exception);
            return exception.Status switch
            {
                400 when code.Contains("content_filter", StringComparison.OrdinalIgnoreCase)
                    || code.Contains("ResponsibleAIPolicyViolation", StringComparison.OrdinalIgnoreCase) => ClinicalAiFailure.ContentFiltered,
                400 or 404 or 422 => ClinicalAiFailure.ConfigurationError,
                401 => ClinicalAiFailure.AuthenticationFailed,
                403 or 429 when code.Contains("quota", StringComparison.OrdinalIgnoreCase) => ClinicalAiFailure.QuotaExceeded,
                403 => ClinicalAiFailure.AuthenticationFailed,
                429 => ClinicalAiFailure.RateLimitedByProvider,
                _ => ClinicalAiFailure.ProviderUnavailable
            };
        }

        // The error's code and inner code ("content_filter", "insufficient_quota"...), read only to categorize: the rest
        // of the body (message, filter results) is never kept.
        private static string ErrorCode(ClientResultException exception)
        {
            try
            {
                if (exception.GetRawResponse()?.Content is not { } content || content.ToMemory().Length == 0)
                {
                    return string.Empty;
                }

                using var body = JsonDocument.Parse(content);
                if (!body.RootElement.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object)
                {
                    return string.Empty;
                }

                static string Code(JsonElement element) =>
                    element.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String ? code.GetString()! : string.Empty;

                var inner = error.TryGetProperty("innererror", out var innerError) && innerError.ValueKind == JsonValueKind.Object ? Code(innerError) : string.Empty;
                var type = error.TryGetProperty("type", out var errorType) && errorType.ValueKind == JsonValueKind.String ? errorType.GetString()! : string.Empty;
                return $"{Code(error)} {inner} {type}";
            }
            catch (Exception unreadable) when (unreadable is JsonException or InvalidOperationException)
            {
                return string.Empty;
            }
        }
    }
}
