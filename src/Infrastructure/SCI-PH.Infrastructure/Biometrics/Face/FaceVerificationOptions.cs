using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace SCI_PH.Infrastructure.Biometrics.Face
{
    // Server-side choice of the face provider (ADR-012). Never chosen by a client: no query or body field selects it, and
    // thresholds live inside each provider's own configuration.
    public sealed class FaceVerificationOptions
    {
        public const string SectionName = "FaceVerification";
        public const string FakeProvider = "Fake";
        public const string DisabledProvider = "Disabled";
        public const string OpenCvProvider = "OpenCv";

        // "Fake" (deterministic: CI and development), "Disabled" (every face operation answers 503) or "OpenCv" (the real
        // local provider, FaceVerification:OpenCv). Not configured: Fake outside Production, Disabled in Production.
        public string? Provider { get; set; }

        public string Resolve(IHostEnvironment environment) =>
            Provider ?? (environment.IsProduction() ? DisabledProvider : FakeProvider);
    }

    internal sealed class FaceVerificationOptionsValidator : IValidateOptions<FaceVerificationOptions>
    {
        private readonly IHostEnvironment _environment;

        public FaceVerificationOptionsValidator(IHostEnvironment environment) => _environment = environment;

        public ValidateOptionsResult Validate(string? name, FaceVerificationOptions options) =>
            options.Resolve(_environment) switch
            {
                FaceVerificationOptions.FakeProvider when _environment.IsProduction() =>
                    ValidateOptionsResult.Fail("FaceVerification:Provider 'Fake' cannot be used in Production: it does not recognize faces."),
                FaceVerificationOptions.FakeProvider or FaceVerificationOptions.DisabledProvider or FaceVerificationOptions.OpenCvProvider =>
                    ValidateOptionsResult.Success,
                var other => ValidateOptionsResult.Fail($"FaceVerification:Provider '{other}' is not supported.")
            };
    }
}
