using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using SCI_PH.Mobile.Common.Authentication;
using SCI_PH.Mobile.Features.Patient.Models.Dtos;
using SCI_PH.Mobile.Features.Patient.Services.Interfaces;

namespace SCI_PH.Mobile.Features.Patient.ViewModels;

/// <summary>Resultados publicados del paciente. Si falla, la lista queda vacía con el mensaje.</summary>
public sealed partial class ResultsViewModel : PatientRecordViewModel
{
    private readonly IPatientDataService _data;

    public ResultsViewModel(IPatientDataService data, IClinicalSessionService session)
        : base(session)
    {
        _data = data;
    }

    public ObservableCollection<ResultItemDto> Results { get; } = [];

    [RelayCommand]
    private Task LoadAsync() => LoadRecordAsync(() => _data.GetResultsAsync(), loaded =>
    {
        foreach (var result in loaded)
        {
            Results.Add(result);
        }
    });

    protected override void ClearRecord() => Results.Clear();
}
