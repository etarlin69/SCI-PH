using CommunityToolkit.Mvvm.Input;
using SCI_PH.Mobile.Common.Navigation;
using SCI_PH.Mobile.Common.ViewModels;
using SCI_PH.Mobile.Features.Paramedic;
using SCI_PH.Mobile.Features.Patient;

namespace SCI_PH.Mobile.Common.Entry;

/// <summary>
/// Entrada compartida: el usuario elige cómo accede. Ninguna de las dos vías autentica todavía:
/// el personal clínico entra al prototipo paramédico (login real pendiente) y el paciente al acceso QR + rostro
/// simulado. Ambas se apilan sobre la entrada, así que "Atrás" permite volver a elegir.
/// </summary>
public partial class EntryViewModel : BaseViewModel
{
    private readonly IAppNavigator _navigator;

    public EntryViewModel(IAppNavigator navigator)
    {
        _navigator = navigator;
    }

    [RelayCommand]
    private Task OpenClinicalStaffAsync() => _navigator.GoToAsync(ParamedicRoutes.NewEmergency);

    [RelayCommand]
    private Task OpenPatientAsync() => _navigator.GoToAsync(PatientRoutes.QrAccess);
}
