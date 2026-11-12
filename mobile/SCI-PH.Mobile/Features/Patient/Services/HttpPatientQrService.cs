using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Features.Patient.Models.Dtos;
using SCI_PH.Mobile.Features.Patient.Services.Interfaces;

namespace SCI_PH.Mobile.Features.Patient.Services;

/// <summary>
/// QR real del paciente: <c>POST api/mobile/patient/me/qr</c>, sin ningún id de paciente. El contenido nunca se escribe en el
/// dispositivo ni se registra: vive solo en memoria y desaparece al cerrar la app. Nunca hay un QR de ejemplo.
/// </summary>
public sealed class HttpPatientQrService : IPatientQrService
{
    private const string Route = "api/mobile/patient/me/qr";
    private const string PayloadPrefix = "sciphqr:v1:";

    private readonly IMobileApiClient _client;

    public HttpPatientQrService(IMobileApiClient client) => _client = client;

    public async Task<PatientQrDto> IssueAsync(CancellationToken cancellationToken = default)
    {
        QrResponse response;
        try
        {
            response = await IssueOnceAsync(cancellationToken);
        }
        catch (MobileApiException exception) when (exception.Error == MobileApiError.Conflict)
        {
            // Otra petición del mismo paciente emitió uno a la vez: este no se emitió. Se reintenta una sola vez.
            response = await IssueOnceAsync(cancellationToken);
        }

        var lifetime = response.ExpiresAtUtc - response.IssuedAtUtc;
        if (!response.Payload.StartsWith(PayloadPrefix, StringComparison.Ordinal) || lifetime <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("The server returned an unexpected QR response.");
        }

        // La duración (15 minutos) sale de las dos marcas de tiempo del servidor, emisión y vencimiento: el reloj del
        // dispositivo puede estar desajustado. El servidor sigue decidiendo si el QR es válido al escanearlo.
        return new PatientQrDto(response.Payload, lifetime);
    }

    private Task<QrResponse> IssueOnceAsync(CancellationToken cancellationToken) =>
        _client.SendAsync<QrResponse>(new HttpRequestMessage(HttpMethod.Post, Route), cancellationToken);

    private sealed record QrResponse(string Payload, DateTimeOffset IssuedAtUtc, DateTimeOffset ExpiresAtUtc)
    {
        public override string ToString() => nameof(QrResponse);
    }
}
