using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;

namespace SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;

/// <summary>
/// Catálogos de procedimientos y medicamentos.
/// PENDIENTE: el catálogo real lo administra Administración (Rol 5); aún no hay endpoint definido.
/// </summary>
public interface IParamedicCatalogService
{
    Task<IReadOnlyList<CatalogItemDto>> GetProceduresAsync();
    Task<IReadOnlyList<CatalogItemDto>> GetMedicationsAsync();
}
