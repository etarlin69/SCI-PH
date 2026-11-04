namespace SCI_PH.Mobile.Features.Patient.Models.Dtos;

/// <summary>
/// El QR vigente del paciente: el contenido opaco que entrega el servidor una sola vez y cuánto dura desde ahora. Vive solo
/// en memoria y su texto nunca se imprime.
/// </summary>
public sealed record PatientQrDto(string Payload, TimeSpan Lifetime)
{
    public override string ToString() => nameof(PatientQrDto);
}
