using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCI_PH.PatientPrototype.Features.Patient.Models.Dtos;
using SCI_PH.PatientPrototype.Features.Patient.Services.Interfaces;

namespace SCI_PH.PatientPrototype.Features.Patient.ViewModels;

/// <summary>Historial clínico: alergias, condiciones, medicación habitual y episodios.</summary>
public sealed partial class HistoryViewModel : PatientViewModel
{
    private readonly IPatientDataService _data;

    public HistoryViewModel(IPatientDataService data)
    {
        _data = data;
    }

    [ObservableProperty]
    private ClinicalHistoryDto? history;

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async cancellationToken =>
        History = await _data.GetHistoryAsync(cancellationToken));
}
