namespace SCI_PH.Mobile.Features.Paramedic.Models.Enums;

/// <summary>
/// Estado de la emergencia según docs/api/mobile-bff-contracts.md (el GET de ejemplo devuelve "Open").
/// PENDIENTE: confirmar el conjunto final de valores con Rol 1 / Rol 2
/// (el dominio usa ClinicalEncounterStatus: Created, InProgress, Transferred, ...).
/// </summary>
public enum EmergencyStatus
{
    Open,
    Transferred,
    Closed
}
