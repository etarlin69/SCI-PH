using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCI_PH.Mobile.Common.Authentication;
using SCI_PH.Mobile.Features.Patient.Models.Dtos;
using SCI_PH.Mobile.Features.Patient.Services.Interfaces;

namespace SCI_PH.Mobile.Features.Patient.ViewModels;

/// <summary>
/// Detalle de una atención propia, abierto desde el historial. El id llega por la navegación y el servidor decide si es
/// del paciente (la de otra persona o una inexistente es el mismo 404, "no disponible"). Como las demás pantallas del
/// expediente, se vacía al cambiar o cerrar la sesión y descarta respuestas tardías; si falla, no muestra datos.
/// </summary>
[QueryProperty(nameof(EncounterId), PatientRoutes.EncounterIdParameter)]
public sealed partial class EncounterDetailViewModel : PatientRecordViewModel
{
    private readonly IPatientDataService _data;

    public EncounterDetailViewModel(IPatientDataService data, IClinicalSessionService session)
        : base(session)
    {
        _data = data;
    }

    [ObservableProperty]
    private Guid encounterId;

    [ObservableProperty]
    private EncounterDetailDto? detail;

    [RelayCommand]
    private Task LoadAsync()
    {
        if (EncounterId == Guid.Empty)
        {
            ClearRecord();
            ErrorMessage = PatientMessages.NotAvailable;
            return Task.CompletedTask;
        }

        var requested = EncounterId;
        return LoadRecordAsync(() => _data.GetEncounterAsync(requested), loaded => Detail = loaded);
    }

    protected override void ClearRecord() => Detail = null;
}
