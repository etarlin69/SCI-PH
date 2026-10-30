using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using SCI_PH.Application.Abstractions.Biometrics;

namespace SCI_PH.Infrastructure.Biometrics.Face.OpenCv
{
    // Builds the local face provider when the host starts. Resolved in StartAsync, NOT in the constructor: the host builds
    // its hosted services before validating options, and the provider must never load a model that the validation
    // (presence and pinned SHA-256) has not accepted yet. Loading the native runtime and the first detector/recognizer pair
    // here makes a broken image fail the start instead of the first request. The template store is NOT contacted: its outage
    // is a 503 per request, not a start failure.
    internal sealed class OpenCvFaceProviderStartup : IHostedService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<OpenCvFaceProviderStartup> _logger;

        public OpenCvFaceProviderStartup(IServiceProvider services, ILogger<OpenCvFaceProviderStartup> logger)
        {
            _services = services;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var faces = _services.GetRequiredService<IFaceVerificationService>();
            _logger.LogInformation("Face provider {Provider} ready: OpenCV {OpenCvVersion}, algorithm {AlgorithmVersion}.",
                faces.Provider, Cv2.GetVersionString(), faces.AlgorithmVersion);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
