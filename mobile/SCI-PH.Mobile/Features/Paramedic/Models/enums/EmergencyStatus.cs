using System.Text.Json.Serialization;

namespace SCI_PH.Mobile.Features.Paramedic.Models.Enums;

/// <summary>
/// Estado de la emergencia según docs/api/mobile-bff-contracts.md (el GET de ejemplo devuelve "Open").
/// PENDIENTE: confirmar el conjunto final de valores con Rol 1 / Rol 2
/// (el dominio usa ClinicalEncounterStatus: Created, InProgress, Transferred, ...).
/// En el contrato viaja como texto: el atributo lo serializa por nombre, de modo que los valores numéricos de este
/// enum no tienen que coincidir con los del Domain.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<EmergencyStatus>))]
public enum EmergencyStatus
{
    Open,
    Transferred,
    Closed,

    /// <summary>
    /// Recibida por el hospital (Received o InEvolution en el servidor): la atención prehospitalaria terminó y la app ya no
    /// registra ni identifica sobre ella.
    /// </summary>
    HandedOff
}
