using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using SCI_PH.PatientPrototype.Features.Patient.Models.Dtos;
using SCI_PH.PatientPrototype.Features.Patient.Services.Interfaces;

namespace SCI_PH.PatientPrototype.Features.Patient.ViewModels;

/// <summary>Resultados publicados del paciente.</summary>
public sealed partial class ResultsViewModel : PatientViewModel
{
    private readonly IPatientDataService _data;

    public ResultsViewModel(IPatientDataService data)
    {
        _data = data;
    }

    public ObservableCollection<ResultItemDto> Results { get; } = [];

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async cancellationToken =>
    {
        var results = await _data.GetResultsAsync(cancellationToken);
        Results.Clear();
        foreach (var result in results)
        {
            Results.Add(result);
        }
    });
}
