using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCI_PH.Mobile.Common.Authentication;
using SCI_PH.Mobile.Features.Patient.Models.Dtos;
using SCI_PH.Mobile.Features.Patient.Services.Interfaces;

namespace SCI_PH.Mobile.Features.Patient.ViewModels;

/// <summary>Inicio: resumen del expediente propio del paciente autenticado. Si falla, no muestra datos.</summary>
public sealed partial class HomeViewModel : PatientRecordViewModel
{
    private readonly IPatientDataService _data;

    public HomeViewModel(IPatientDataService data, IClinicalSessionService session)
        : base(session)
    {
        _data = data;
    }

    [ObservableProperty]
    private PatientSummaryDto? summary;

    [RelayCommand]
    private Task LoadAsync() => LoadRecordAsync(() => _data.GetSummaryAsync(), loaded => Summary = loaded);

    protected override void ClearRecord() => Summary = null;
}
