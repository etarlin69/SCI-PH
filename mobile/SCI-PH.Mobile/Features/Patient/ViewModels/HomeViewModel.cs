using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCI_PH.Mobile.Common.Navigation;
using SCI_PH.Mobile.Common.ViewModels;
using SCI_PH.Mobile.Features.Patient.Models.Dtos;
using SCI_PH.Mobile.Features.Patient.Services.Interfaces;

namespace SCI_PH.Mobile.Features.Patient.ViewModels;

/// <summary>Inicio: resumen del paciente autenticado y acceso al asistente.</summary>
public sealed partial class HomeViewModel : BaseViewModel
{
    private readonly IPatientDataService _data;
    private readonly IAppNavigator _navigator;

    public HomeViewModel(IPatientDataService data, IAppNavigator navigator)
    {
        _data = data;
        _navigator = navigator;
    }

    [ObservableProperty]
    private PatientSummaryDto? summary;

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () =>
        Summary = await _data.GetSummaryAsync(), GenericErrorMessage);

    [RelayCommand]
    private Task OpenAssistantAsync() => _navigator.GoToAsync(AppRoutes.Root(PatientRoutes.Assistant));
}
