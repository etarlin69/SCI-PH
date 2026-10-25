using System.Text.Json.Serialization;

namespace SCI_PH.Mobile.Features.Paramedic.Models.Enums;

/// <summary>
/// Tipo de documento para identificar al paciente: "NationalId | Passport" en el contrato (los nombres del enum del
/// servidor). Viaja como texto, de modo que los valores numéricos de este enum no tienen que coincidir con los del Domain.
/// Otros tipos no demuestran identidad o no se digitan, y el servidor los rechaza.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PatientDocumentType>))]
public enum PatientDocumentType
{
    NationalId,
    Passport
}
