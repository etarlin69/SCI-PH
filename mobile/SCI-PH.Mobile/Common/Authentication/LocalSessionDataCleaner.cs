namespace SCI_PH.Mobile.Common.Authentication;

/// <summary>Borra del dispositivo lo que una sesión anterior pudiera haber dejado fuera de <see cref="ISecureTokenStore"/>.</summary>
public interface ILocalSessionDataCleaner
{
    /// <summary>Nunca lanza: limpiar no puede impedir un cierre de sesión ni un arranque.</summary>
    void Clear();
}

/// <summary>
/// La foto y la descripción de perfil de demostración ya no existen, pero versiones anteriores de la app las guardaban
/// sin distinguir cuentas (Preferences y un archivo en AppData). Se borran al arrancar y en toda limpieza de sesión, para
/// que nada de un paciente quede en el dispositivo.
/// </summary>
public sealed class LegacyProfileDataCleaner : ILocalSessionDataCleaner
{
    public const string DescriptionKey = "patient_profile_description";
    public const string PhotoKey = "patient_profile_photo";
    public const string PhotoFilePattern = "patient_profile_photo.*";

    private readonly IPreferences _preferences;
    private readonly string _appDataDirectory;

    public LegacyProfileDataCleaner(IPreferences preferences, string appDataDirectory)
    {
        _preferences = preferences;
        _appDataDirectory = appDataDirectory;
    }

    public void Clear()
    {
        try
        {
            _preferences.Remove(DescriptionKey);
            _preferences.Remove(PhotoKey);
        }
        catch (Exception)
        {
            // Sin esas claves la app no las lee; los archivos se intentan borrar igualmente.
        }

        try
        {
            if (!Directory.Exists(_appDataDirectory))
            {
                return;
            }

            foreach (var photo in Directory.EnumerateFiles(_appDataDirectory, PhotoFilePattern))
            {
                File.Delete(photo);
            }
        }
        catch (Exception)
        {
            // Se reintenta en la siguiente limpieza.
        }
    }
}
