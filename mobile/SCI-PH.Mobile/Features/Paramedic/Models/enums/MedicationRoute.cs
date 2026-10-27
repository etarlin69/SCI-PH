using System.Text.Json.Serialization;

namespace SCI_PH.Mobile.Features.Paramedic.Models.Enums;

/// <summary>
/// Vía de administración: los nombres del enum <c>MedicationRoute</c> del servidor, que viajan como texto. Los valores
/// numéricos de este enum no tienen que coincidir con los del Domain. Las etiquetas en español son solo de la UI.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<MedicationRoute>))]
public enum MedicationRoute
{
    Oral,
    Intravenous,
    Intramuscular,
    Subcutaneous,
    Inhalation,
    Topical,
    Sublingual,
    Rectal,
    Nasal,
    Ophthalmic,
    Otic,
    Transdermal,
    Other
}
