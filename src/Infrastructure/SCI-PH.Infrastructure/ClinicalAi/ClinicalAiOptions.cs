using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace SCI_PH.Infrastructure.ClinicalAi
{
    // Server-side choice of the clinical AI provider (ADR-013). Never chosen by a client: no query or body field selects
    // the provider, the model, the prompt or a limit.
    public sealed class ClinicalAiOptions
    {
        public const string SectionName = "ClinicalAi";
        public const string FakeProvider = "Fake";
        public const string DisabledProvider = "Disabled";
        // Reserved for the real provider (PR 2); not accepted yet.
        public const string AzureOpenAiProvider = "AzureOpenAI";

        // "Fake" (deterministic, in process: CI and development) or "Disabled" (every operation answers 503). Not
        // configured: Fake outside Production, Disabled in Production.
        public string? Provider { get; set; }

        // Upper bound of one provider call.
        public int TimeoutSeconds { get; set; } = 30;

        public FakeClinicalAiOptions Fake { get; set; } = new();

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
                ClinicalAiOptions.AzureOpenAiProvider =>
                    ValidateOptionsResult.Fail("ClinicalAi:Provider 'AzureOpenAI' is not available yet."),
                var other => ValidateOptionsResult.Fail($"ClinicalAi:Provider '{other}' is not supported.")
            };
        }
    }
}
