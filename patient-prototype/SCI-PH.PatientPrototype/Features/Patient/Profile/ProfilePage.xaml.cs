using Microsoft.Maui.Storage;

namespace SCI_PH.PatientPrototype.Features.Patient.Profile;

public partial class ProfilePage : ContentPage
{
    private const string DescriptionKey = "patient_profile_description";
    private const string PhotoKey = "patient_profile_photo";

    public ProfilePage()
    {
        InitializeComponent();
        LoadProfileData();
    }

    private void LoadProfileData()
    {
        string savedDescription = Preferences.Default.Get(
            DescriptionKey,
            "Paciente de SCI-PH. Aquí puedes escribir una breve descripción personal.");

        DescriptionEditor.Text = savedDescription;
        PreviewDescriptionLabel.Text = savedDescription;

        string savedPhotoPath = Preferences.Default.Get(
            PhotoKey,
            string.Empty);

        if (!string.IsNullOrWhiteSpace(savedPhotoPath) &&
            File.Exists(savedPhotoPath))
        {
            ProfileImage.Source =
                ImageSource.FromFile(savedPhotoPath);
        }
        else
        {
            ProfileImage.Source = "patient_avatar.png";
        }
    }

    private async void OnChangePhotoClicked(
        object sender,
        EventArgs e)
    {
        try
        {
            FileResult? selectedPhoto =
                await MediaPicker.Default.PickPhotoAsync();

            if (selectedPhoto == null)
                return;

            string extension =
                Path.GetExtension(selectedPhoto.FileName);

            if (string.IsNullOrWhiteSpace(extension))
                extension = ".jpg";

            string localFileName =
                $"patient_profile_photo{extension}";

            string localPath =
                Path.Combine(
                    FileSystem.AppDataDirectory,
                    localFileName);

            await using Stream sourceStream =
                await selectedPhoto.OpenReadAsync();

            await using FileStream localFileStream =
                File.Open(
                    localPath,
                    FileMode.Create,
                    FileAccess.Write);

            await sourceStream.CopyToAsync(localFileStream);

            Preferences.Default.Set(
                PhotoKey,
                localPath);

            ProfileImage.Source =
                ImageSource.FromFile(localPath);

            await DisplayAlert(
                "Foto actualizada",
                "Tu foto de perfil fue actualizada correctamente.",
                "OK");
        }
        catch (PermissionException)
        {
            await DisplayAlert(
                "Permiso requerido",
                "SCI-PH necesita permiso para acceder a tus fotografías.",
                "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Error",
                $"No se pudo cambiar la foto.\n\n{ex.Message}",
                "OK");
        }
    }

    private async void OnSaveDescriptionClicked(
        object sender,
        EventArgs e)
    {
        string description =
            DescriptionEditor.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(description))
        {
            await DisplayAlert(
                "Descripción vacía",
                "Escribe una descripción antes de guardar.",
                "OK");

            return;
        }

        Preferences.Default.Set(
            DescriptionKey,
            description);

        PreviewDescriptionLabel.Text =
            description;

        await DisplayAlert(
            "Perfil actualizado",
            "La descripción fue guardada correctamente.",
            "OK");
    }
}