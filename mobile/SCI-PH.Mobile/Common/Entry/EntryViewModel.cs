using CommunityToolkit.Mvvm.Input;
using SCI_PH.Mobile.Common.Authentication;
using SCI_PH.Mobile.Common.Navigation;
using SCI_PH.Mobile.Common.ViewModels;
using SCI_PH.Mobile.Features.Auth;
using SCI_PH.Mobile.Features.Patient;

namespace SCI_PH.Mobile.Common.Entry;

/// <summary>
/// Entrada compartida: el usuario elige cómo accede. El personal clínico inicia sesión contra el Mobile BFF (si ya
/// tiene una sesión válida con permiso, vuelve directamente al área paramédica); el paciente va al acceso QR + rostro
/// simulado. Ambas vías se apilan sobre la entrada, así que "Atrás" permite volver a elegir.
/// </summary>
public partial class EntryViewModel : BaseViewModel
{
    private readonly IAppNavigator _navigator;
    private readonly IClinicalSessionService _clinicalSession;

    public EntryViewModel(IAppNavigator navigator, IClinicalSessionService clinicalSession)
    {
        _navigator = navigator;
        _clinicalSession = clinicalSession;
    }

    [RelayCommand]
    private Task OpenClinicalStaffAsync() =>
        _navigator.GoToAsync(_clinicalSession.CanOperateEmergencies ? AuthRoutes.ClinicalArea : AuthRoutes.Login);

    [RelayCommand]
    private Task OpenPatientAsync() => _navigator.GoToAsync(PatientRoutes.QrAccess);
}
