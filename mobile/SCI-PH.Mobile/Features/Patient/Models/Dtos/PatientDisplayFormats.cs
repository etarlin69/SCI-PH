using System.Globalization;

namespace SCI_PH.Mobile.Features.Patient.Models.Dtos;

/// <summary>Formatos de fecha en español para las pantallas del paciente, independientes del idioma del dispositivo.</summary>
public static class PatientDisplayFormats
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");

    /// <summary>"02 octubre 2026".</summary>
    public static string ShortDate(DateOnly date) => date.ToString("dd MMMM yyyy", Spanish);

    /// <summary>"15 de junio de 2000".</summary>
    public static string LongDate(DateOnly date) => date.ToString("d 'de' MMMM 'de' yyyy", Spanish);
}
