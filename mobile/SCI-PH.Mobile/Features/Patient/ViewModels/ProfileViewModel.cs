using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCI_PH.Mobile.Common.Authentication;
using SCI_PH.Mobile.Features.Patient.Models.Dtos;
using SCI_PH.Mobile.Features.Patient.Services.Interfaces;

namespace SCI_PH.Mobile.Features.Patient.ViewModels;

/// <summary>Perfil de solo lectura: todo viene del expediente propio en el servidor; nada se guarda en el dispositivo.</summary>
public sealed partial class ProfileViewModel : PatientRecordViewModel
{
    private readonly IPatientDataService _data;

    public ProfileViewModel(IPatientDataService data, IClinicalSessionService session)
        : base(session)
    {
        _data = data;
    }

    [ObservableProperty]
    private PatientProfileDto? profile;

    [RelayCommand]
    private Task LoadAsync() => LoadRecordAsync(() => _data.GetProfileAsync(), loaded => Profile = loaded);

    protected override void ClearRecord() => Profile = null;
}
