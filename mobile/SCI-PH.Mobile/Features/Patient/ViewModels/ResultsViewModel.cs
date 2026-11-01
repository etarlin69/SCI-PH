using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using SCI_PH.Mobile.Common.ViewModels;
using SCI_PH.Mobile.Features.Patient.Models.Dtos;
using SCI_PH.Mobile.Features.Patient.Services.Interfaces;

namespace SCI_PH.Mobile.Features.Patient.ViewModels;

/// <summary>Resultados publicados del paciente. Si falla, la lista queda vacía con el mensaje.</summary>
public sealed partial class ResultsViewModel : BaseViewModel
{
    private readonly IPatientDataService _data;

    public ResultsViewModel(IPatientDataService data)
    {
        _data = data;
    }

    public ObservableCollection<ResultItemDto> Results { get; } = [];

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () =>
    {
        Results.Clear();
        foreach (var result in await _data.GetResultsAsync())
        {
            Results.Add(result);
        }
    }, PatientMessages.For);
}
