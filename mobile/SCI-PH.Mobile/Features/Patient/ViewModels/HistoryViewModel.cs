using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCI_PH.Mobile.Common.Authentication;
using SCI_PH.Mobile.Common.Navigation;
using SCI_PH.Mobile.Features.Patient.Models.Dtos;
using SCI_PH.Mobile.Features.Patient.Services.Interfaces;

namespace SCI_PH.Mobile.Features.Patient.ViewModels;

/// <summary>
/// Historial clínico propio: alergias, diagnósticos, recetas y atenciones; cada atención abre su detalle. Si falla, no
/// muestra datos.
/// </summary>
public sealed partial class HistoryViewModel : PatientRecordViewModel
{
    private readonly IPatientDataService _data;
    private readonly IAppNavigator _navigator;

    public HistoryViewModel(IPatientDataService data, IClinicalSessionService session, IAppNavigator navigator)
        : base(session)
    {
        _data = data;
        _navigator = navigator;
    }

    [ObservableProperty]
    private ClinicalHistoryDto? history;

    [RelayCommand]
    private Task LoadAsync() => LoadRecordAsync(() => _data.GetHistoryAsync(), loaded => History = loaded);

    // Solo el id de la atención viaja: el detalle lo pide al servidor, que comprueba que es del paciente.
    [RelayCommand]
    private Task OpenEncounterAsync(EpisodeItemDto? episode) =>
        episode is null || episode.Id == Guid.Empty
            ? Task.CompletedTask
            : _navigator.GoToAsync(PatientRoutes.EncounterDetail, new Dictionary<string, object> { [PatientRoutes.EncounterIdParameter] = episode.Id });

    protected override void ClearRecord() => History = null;
}
