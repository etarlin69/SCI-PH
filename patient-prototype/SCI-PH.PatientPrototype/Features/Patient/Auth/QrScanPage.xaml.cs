namespace SCI_PH.PatientPrototype.Features.Patient.Auth;

public partial class QrScanPage : ContentPage
{
    public QrScanPage()
    {
        InitializeComponent();
    }

    private async void OnSimulateQrClicked(object sender, EventArgs e)
    {
        StatusLabel.Text = "QR válido. Challenge generado correctamente.";

        await Task.Delay(500);

        await Shell.Current.GoToAsync(nameof(FaceVerifyPage));
    }
}