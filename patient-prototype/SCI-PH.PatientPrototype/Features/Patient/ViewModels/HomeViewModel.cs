using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCI_PH.PatientPrototype.Features.Patient.Models.Dtos;
using SCI_PH.PatientPrototype.Features.Patient.Services.Interfaces;

namespace SCI_PH.PatientPrototype.Features.Patient.ViewModels;

/// <summary>Inicio: resumen del paciente autenticado y acceso al asistente.</summary>
public sealed partial class HomeViewModel : PatientViewModel
{
    private readonly IPatientDataService _data;
    private readonly IPatientNavigator _navigator;

    public HomeViewModel(IPatientDataService data, IPatientNavigator navigator)
    {
        _data = data;
        _navigator = navigator;
    }

    [ObservableProperty]
    private PatientSummaryDto? summary;

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async cancellationToken =>
        Summary = await _data.GetSummaryAsync(cancellationToken));

    [RelayCommand]
    private Task OpenAssistantAsync() => _navigator.GoToAsync(PatientRoutes.Root(PatientRoutes.Assistant));
}
