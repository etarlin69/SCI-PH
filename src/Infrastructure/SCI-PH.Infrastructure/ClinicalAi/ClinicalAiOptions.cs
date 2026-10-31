using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace SCI_PH.Infrastructure.ClinicalAi
{
    // Server-side choice of the clinical AI provider (ADR-013, ADR-014). Never chosen by a client: no query or body field
    // selects the provider, the model, the deployment, the prompt or a limit.
    public sealed class ClinicalAiOptions
    {
        public const string SectionName = "ClinicalAi";
        public const string FakeProvider = "Fake";
        public const string DisabledProvider = "Disabled";
        // External: the minimized clinical input leaves SCI-PH (ADR-014).
        public const string AzureOpenAiProvider = "AzureOpenAI";

        // "Fake" (deterministic, in process: CI and development), "Disabled" (every operation answers 503) or "AzureOpenAI"
        // (external, requires AllowExternalClinicalData). Not configured: Fake outside Production, Disabled in Production.
        public string? Provider { get; set; }

        // The deliberate, separate permission to send clinical data outside SCI-PH. A provider that does so does not start
        // without it, so a provider setting alone can never export data by accident. It is NOT an approval to send real
        // patient data: demo and academic use is synthetic data only; real production data needs the institutional,
        // legal, privacy and compliance review of ADR-014 first.
        public bool AllowExternalClinicalData { get; set; }

        // Upper bound of one operation (every provider call and its single retry or regeneration included).
        public int TimeoutSeconds { get; set; } = 30;

        public FakeClinicalAiOptions Fake { get; set; } = new();

        public AzureOpenAiClinicalAiOptions AzureOpenAI { get; set; } = new();

        public string Resolve(IHostEnvironment environment) =>
            Provider ?? (environment.IsProduction() ? DisabledProvider : FakeProvider);
    }

    // Deterministic failure modes of the Fake for tests and the end-to-end (never available in Production, where the Fake
    // itself cannot start). Delay makes a call last, e.g. to observe the concurrency limit.
    public sealed class FakeClinicalAiOptions
    {
        public FakeClinicalAiScenario Scenario { get; set; } = FakeClinicalAiScenario.Normal;

        public int DelayMilliseconds { get; set; }
    }

    // Azure OpenAI (Microsoft Foundry) deployment of the summary model (ADR-014). The deployment name is internal
    // configuration: no response, audit entry or client sees it (they see ModelVersion). Secrets (ApiKey) only from User
    // Secrets or environment variables, never appsettings or the repository; never logged or audited.
    public sealed class AzureOpenAiClinicalAiOptions
    {
        public const string DefaultModelVersion = "gpt-5.6-luna-2026-07-09";
        public const string DefaultTokenScope = "https://cognitiveservices.azure.com/.default";
        public const int DefaultMaxCompletionTokens = 6000;

        // The resource endpoint ("https://<resource>.openai.azure.com/"); requests go to its "openai/v1/" API.
        public string? Endpoint { get; set; }

        public string? Deployment { get; set; }

        // The model and version deployed (recorded in every answer and audit entry instead of the deployment name).
        public string ModelVersion { get; set; } = DefaultModelVersion;

        public AzureOpenAiAuthenticationMode AuthenticationMode { get; set; } = AzureOpenAiAuthenticationMode.ManagedIdentity;

        // Development only (refused in Production).
        public string? ApiKey { get; set; }

        // A user-assigned managed identity; empty: the system-assigned one.
        public string? ManagedIdentityClientId { get; set; }

        // Microsoft Entra ID scope of the token.
        public string TokenScope { get; set; } = DefaultTokenScope;

        // Upper bound of the answer, reasoning tokens included (max_completion_tokens).
        public int MaxCompletionTokens { get; set; } = DefaultMaxCompletionTokens;
    }

    public enum AzureOpenAiAuthenticationMode
    {
        // Microsoft Entra ID with the host's managed identity: the only mode allowed in Production.
        ManagedIdentity = 0,
        // Development: the developer's Entra ID sign-in (Azure CLI, Visual Studio...) through DefaultAzureCredential.
        DefaultAzureCredential = 1,
        // Development: a resource key from User Secrets or an environment variable.
        ApiKey = 2
    }

    public enum FakeClinicalAiScenario
    {
        Normal = 0,
        ProviderUnavailable = 1,
        Timeout = 2,
        InvalidSchema = 3,
        UnknownAlias = 4,
        OversizedOutput = 5
    }

    internal sealed class ClinicalAiOptionsValidator : IValidateOptions<ClinicalAiOptions>
    {
        public const int MaxTimeoutSeconds = 120;
        public const int MinCompletionTokens = 1000;
        public const int MaxCompletionTokens = 32000;

        private readonly IHostEnvironment _environment;

        public ClinicalAiOptionsValidator(IHostEnvironment environment) => _environment = environment;

        public ValidateOptionsResult Validate(string? name, ClinicalAiOptions options)
        {
            if (options.TimeoutSeconds is < 1 or > MaxTimeoutSeconds)
            {
                return ValidateOptionsResult.Fail($"ClinicalAi:TimeoutSeconds must be between 1 and {MaxTimeoutSeconds}.");
            }

            if (options.Fake.DelayMilliseconds is < 0 or > MaxTimeoutSeconds * 1000)
            {
                return ValidateOptionsResult.Fail("ClinicalAi:Fake:DelayMilliseconds is out of range.");
            }

            return options.Resolve(_environment) switch
            {
                ClinicalAiOptions.FakeProvider when _environment.IsProduction() =>
                    ValidateOptionsResult.Fail("ClinicalAi:Provider 'Fake' cannot be used in Production: it does not summarize clinical data."),
                ClinicalAiOptions.FakeProvider or ClinicalAiOptions.DisabledProvider => ValidateOptionsResult.Success,
                ClinicalAiOptions.AzureOpenAiProvider => ValidateAzureOpenAi(options),
                var other => ValidateOptionsResult.Fail($"ClinicalAi:Provider '{other}' is not supported.")
            };
        }

        // Never echoes a configured value (the key, the endpoint or the deployment): only which setting is wrong.
        private ValidateOptionsResult ValidateAzureOpenAi(ClinicalAiOptions options)
        {
            if (!options.AllowExternalClinicalData)
            {
                return ValidateOptionsResult.Fail(
                    "ClinicalAi:Provider 'AzureOpenAI' sends clinical data outside SCI-PH: it starts only with ClinicalAi:AllowExternalClinicalData=true, "
                    + "set deliberately (synthetic data only until the review of ADR-014).");
            }

            var azure = options.AzureOpenAI;
            if (!Uri.TryCreate(azure.Endpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps
                || endpoint.AbsolutePath != "/" || endpoint.Query.Length > 0 || endpoint.UserInfo.Length > 0)
            {
                return ValidateOptionsResult.Fail("ClinicalAi:AzureOpenAI:Endpoint must be the https resource endpoint (no path, query or credentials).");
            }

            if (string.IsNullOrWhiteSpace(azure.Deployment) || azure.Deployment.Length > 64
                || !azure.Deployment.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
            {
                return ValidateOptionsResult.Fail("ClinicalAi:AzureOpenAI:Deployment is required (letters, digits, '-', '_' or '.').");
            }

            if (string.IsNullOrWhiteSpace(azure.ModelVersion))
            {
                return ValidateOptionsResult.Fail("ClinicalAi:AzureOpenAI:ModelVersion is required.");
            }

            if (azure.MaxCompletionTokens is < MinCompletionTokens or > MaxCompletionTokens)
            {
                return ValidateOptionsResult.Fail($"ClinicalAi:AzureOpenAI:MaxCompletionTokens must be between {MinCompletionTokens} and {MaxCompletionTokens}.");
            }

            switch (azure.AuthenticationMode)
            {
                case AzureOpenAiAuthenticationMode.ApiKey when _environment.IsProduction():
                    return ValidateOptionsResult.Fail("ClinicalAi:AzureOpenAI:AuthenticationMode 'ApiKey' cannot be used in Production: use ManagedIdentity.");
                case AzureOpenAiAuthenticationMode.DefaultAzureCredential when _environment.IsProduction():
                    return ValidateOptionsResult.Fail(
                        "ClinicalAi:AzureOpenAI:AuthenticationMode 'DefaultAzureCredential' cannot be used in Production: use ManagedIdentity.");
                case AzureOpenAiAuthenticationMode.ApiKey when string.IsNullOrWhiteSpace(azure.ApiKey):
                    return ValidateOptionsResult.Fail("ClinicalAi:AzureOpenAI:ApiKey is required with AuthenticationMode 'ApiKey' (User Secrets or environment).");
                case AzureOpenAiAuthenticationMode.ApiKey:
                    return ValidateOptionsResult.Success;
                case AzureOpenAiAuthenticationMode.ManagedIdentity or AzureOpenAiAuthenticationMode.DefaultAzureCredential:
                    if (!string.IsNullOrEmpty(azure.ApiKey))
                    {
                        return ValidateOptionsResult.Fail("ClinicalAi:AzureOpenAI:ApiKey must not be configured with Microsoft Entra ID authentication.");
                    }

                    return Uri.TryCreate(azure.TokenScope, UriKind.Absolute, out var scope) && scope.Scheme == Uri.UriSchemeHttps
                        && azure.TokenScope.EndsWith("/.default", StringComparison.Ordinal)
                        ? ValidateOptionsResult.Success
                        : ValidateOptionsResult.Fail("ClinicalAi:AzureOpenAI:TokenScope must be an https '/.default' scope.");
                default:
                    return ValidateOptionsResult.Fail("ClinicalAi:AzureOpenAI:AuthenticationMode is not supported.");
            }
        }
    }
}
