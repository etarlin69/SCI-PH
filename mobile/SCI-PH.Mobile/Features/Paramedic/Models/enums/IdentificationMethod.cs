using System.Text.Json.Serialization;

namespace SCI_PH.Mobile.Features.Paramedic.Models.Enums;

/// <summary>
/// Método de identificación: "Document | Fingerprint | Manual" en el contrato.
/// En el contrato viaja como texto: el atributo lo serializa por nombre, de modo que los valores numéricos de este
/// enum no tienen que coincidir con los del Domain.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<IdentificationMethod>))]
public enum IdentificationMethod
{
    Document,
    Fingerprint,
    Manual
}
