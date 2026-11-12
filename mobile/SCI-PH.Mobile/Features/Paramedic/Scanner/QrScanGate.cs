using ZXing.Net.Maui;

namespace SCI_PH.Mobile.Features.Paramedic.Scanner;

/// <summary>
/// Primera lectura válida, una sola vez. ZXing avisa desde el hilo de la cámara y repite el mismo código en cada cuadro: solo
/// el primer resultado QR pasa (comparación atómica) y los demás se ignoran hasta <see cref="Rearm"/>. Cualquier otro formato
/// se ignora aunque la cámara lo detecte. El valor sale tal cual: sin recortar ni normalizar.
/// </summary>
public sealed class QrScanGate
{
    private int _taken;

    /// <summary>El contenido del primer QR si este aviso es el que gana; null en cualquier otro caso.</summary>
    public string? TryAccept(IEnumerable<BarcodeResult>? results)
    {
        var value = FirstQrValue(results);
        if (value is null)
        {
            return null;
        }

        return Interlocked.CompareExchange(ref _taken, 1, 0) == 0 ? value : null;
    }

    /// <summary>Vuelve a aceptar una lectura (solo cuando el escáner se reactiva).</summary>
    public void Rearm() => Volatile.Write(ref _taken, 0);

    public static string? FirstQrValue(IEnumerable<BarcodeResult>? results) =>
        results?.FirstOrDefault(r => r is not null && r.Format == BarcodeFormat.QrCode && !string.IsNullOrEmpty(r.Value))?.Value;
}
