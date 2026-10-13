using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCI_PH.PatientPrototype.Features.Patient.Models.Dtos;
using SCI_PH.PatientPrototype.Features.Patient.Services.Interfaces;

namespace SCI_PH.PatientPrototype.Features.Patient.ViewModels;

/// <summary>
/// Perfil: los datos de la cuenta vienen de <see cref="IPatientDataService"/>. La foto y la descripción son una
/// demostración local del prototipo (solo este dispositivo); en Preferences nunca se guardan tokens ni datos clínicos.
/// </summary>
public sealed partial class ProfileViewModel : PatientViewModel
{
    public const string DefaultPhoto = "patient_avatar.png";
    public const string DefaultDescription = "Paciente de SCI-PH. Aquí puedes escribir una breve descripción personal.";

    private const string DescriptionKey = "patient_profile_description";
    private const string PhotoKey = "patient_profile_photo";

    private readonly IPatientDataService _data;
    private readonly IPatientDialogService _dialogs;
    private readonly IPreferences _preferences;
    private readonly IMediaPicker _mediaPicker;
    private readonly IFileSystem _fileSystem;

    public ProfileViewModel(
        IPatientDataService data,
        IPatientDialogService dialogs,
        IPreferences preferences,
        IMediaPicker mediaPicker,
        IFileSystem fileSystem)
    {
        _data = data;
        _dialogs = dialogs;
        _preferences = preferences;
        _mediaPicker = mediaPicker;
        _fileSystem = fileSystem;
    }

    [ObservableProperty]
    private PatientProfileDto? profile;

    [ObservableProperty]
    private string photoSource = DefaultPhoto;

    /// <summary>Texto del editor.</summary>
    [ObservableProperty]
    private string? descriptionDraft;

    /// <summary>Descripción guardada.</summary>
    [ObservableProperty]
    private string description = DefaultDescription;

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async cancellationToken =>
    {
        Profile = await _data.GetProfileAsync(cancellationToken);

        Description = _preferences.Get(DescriptionKey, DefaultDescription);
        DescriptionDraft = Description;

        string savedPhoto = _preferences.Get(PhotoKey, string.Empty);
        PhotoSource = !string.IsNullOrWhiteSpace(savedPhoto) && File.Exists(savedPhoto) ? savedPhoto : DefaultPhoto;
    });

    [RelayCommand]
    private async Task ChangePhotoAsync()
    {
        try
        {
            FileResult? selected = await _mediaPicker.PickPhotoAsync();
            if (selected is null)
            {
                return;
            }

            string extension = Path.GetExtension(selected.FileName);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".jpg";
            }

            string localPath = Path.Combine(_fileSystem.AppDataDirectory, $"patient_profile_photo{extension}");
            await using (Stream source = await selected.OpenReadAsync())
            await using (FileStream target = File.Open(localPath, FileMode.Create, FileAccess.Write))
            {
                await source.CopyToAsync(target);
            }

            _preferences.Set(PhotoKey, localPath);
            // Misma ruta que la foto anterior: se reinicia el origen para que la imagen se recargue.
            PhotoSource = DefaultPhoto;
            PhotoSource = localPath;
            await _dialogs.ShowAsync("Foto actualizada", "Tu foto de perfil fue actualizada correctamente.");
        }
        catch (PermissionException)
        {
            await _dialogs.ShowAsync("Permiso requerido", "SCI-PH necesita permiso para acceder a tus fotografías.");
        }
        catch (Exception)
        {
            await _dialogs.ShowAsync("Error", GenericErrorMessage);
        }
    }

    [RelayCommand]
    private async Task SaveDescriptionAsync()
    {
        string description = DescriptionDraft?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(description))
        {
            await _dialogs.ShowAsync("Descripción vacía", "Escribe una descripción antes de guardar.");
            return;
        }

        _preferences.Set(DescriptionKey, description);
        Description = description;
        await _dialogs.ShowAsync("Perfil actualizado", "La descripción fue guardada correctamente.");
    }
}
