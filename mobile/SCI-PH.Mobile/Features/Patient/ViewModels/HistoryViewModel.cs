using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCI_PH.Mobile.Common.ViewModels;
using SCI_PH.Mobile.Features.Patient.Models.Dtos;
using SCI_PH.Mobile.Features.Patient.Services.Interfaces;

namespace SCI_PH.Mobile.Features.Patient.ViewModels;

/// <summary>Historial clínico propio: alergias, diagnósticos, recetas y atenciones. Si falla, no muestra datos.</summary>
public sealed partial class HistoryViewModel : BaseViewModel
{
    private readonly IPatientDataService _data;

    public HistoryViewModel(IPatientDataService data)
    {
        _data = data;
    }

    [ObservableProperty]
    private ClinicalHistoryDto? history;

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () =>
    {
        History = null;
        History = await _data.GetHistoryAsync();
    }, PatientMessages.For);
}
