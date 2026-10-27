using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;

namespace SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;

/// <summary>
/// Catálogos del servidor para el área paramédica: procedimientos (catálogo fijo SCI-PH-PREHOSP), medicamentos activos y
/// clínicas activas como destino de traslado.
/// </summary>
public interface IParamedicCatalogService
{
    Task<IReadOnlyList<CatalogItemDto>> GetProceduresAsync();
    Task<IReadOnlyList<MedicationCatalogItemDto>> GetMedicationsAsync();
    Task<IReadOnlyList<TransferDestinationDto>> GetTransferDestinationsAsync();
}
