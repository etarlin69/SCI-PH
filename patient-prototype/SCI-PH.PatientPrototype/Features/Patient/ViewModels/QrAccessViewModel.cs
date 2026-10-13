using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCI_PH.PatientPrototype.Features.Patient.Services.Interfaces;

namespace SCI_PH.PatientPrototype.Features.Patient.ViewModels;

/// <summary>
/// Paso 1 del acceso: el QR solo identifica e inicia el challenge; nunca autentica por sí mismo.
/// Simulado hasta que exista <c>POST /api/mobile/auth/qr/challenge</c>.
/// </summary>
public sealed partial class QrAccessViewModel : PatientViewModel
{
    private readonly IPatientNavigator _navigator;

    public QrAccessViewModel(IPatientNavigator navigator)
    {
        _navigator = navigator;
    }

    [ObservableProperty]
    private string? statusMessage;

    [RelayCommand]
    private Task SimulateQrAsync() => RunAsync(async cancellationToken =>
    {
        StatusMessage = "QR válido. Challenge generado correctamente.";
        await Task.Delay(500, cancellationToken);
        await _navigator.GoToAsync(PatientRoutes.FaceVerify);
    });
}
