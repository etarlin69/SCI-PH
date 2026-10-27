using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;

namespace SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;

/// <summary>
/// Catálogos del servidor para el área paramédica: procedimientos (catálogo fijo SCI-PH-PREHOSP) y medicamentos activos.
/// </summary>
public interface IParamedicCatalogService
{
    Task<IReadOnlyList<CatalogItemDto>> GetProceduresAsync();
    Task<IReadOnlyList<MedicationCatalogItemDto>> GetMedicationsAsync();
}
