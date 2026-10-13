using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using SCI_PH.Mobile.Common.ViewModels;
using SCI_PH.Mobile.Features.Patient.Models.Dtos;
using SCI_PH.Mobile.Features.Patient.Services.Interfaces;

namespace SCI_PH.Mobile.Features.Patient.ViewModels;

/// <summary>Resultados publicados del paciente.</summary>
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
        var results = await _data.GetResultsAsync();
        Results.Clear();
        foreach (var result in results)
        {
            Results.Add(result);
        }
    }, GenericErrorMessage);
}
