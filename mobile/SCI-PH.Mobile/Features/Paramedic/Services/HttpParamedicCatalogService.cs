using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;
using SCI_PH.Mobile.Features.Paramedic.Models.Enums;
using SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;

namespace SCI_PH.Mobile.Features.Paramedic.Services;

/// <summary>
/// Catálogos REALES del área paramédica: procedimientos (GET /api/mobile/catalogs/procedures, catálogo fijo del servidor)
/// y medicamentos activos (GET /api/mobile/catalogs/medications). Los fallos llegan como <see cref="MobileApiException"/>.
/// </summary>
public sealed class HttpParamedicCatalogService : IParamedicCatalogService
{
    private const string ProceduresCatalog = "api/mobile/catalogs/procedures";
    private const string MedicationsCatalog = "api/mobile/catalogs/medications";

    private readonly IMobileApiClient _client;

    public HttpParamedicCatalogService(IMobileApiClient client) => _client = client;

    public async Task<IReadOnlyList<CatalogItemDto>> GetProceduresAsync() =>
        (await _client.GetAsync<List<ProcedureCatalogItem>>(ProceduresCatalog))
            .Select(item => new CatalogItemDto { Code = item.Code, Name = item.Name, Description = item.Description })
            .ToList();

    public async Task<IReadOnlyList<MedicationCatalogItemDto>> GetMedicationsAsync() =>
        (await _client.GetAsync<List<MedicationCatalogItem>>(MedicationsCatalog))
            .Select(item => new MedicationCatalogItemDto
            {
                Id = item.Id,
                Code = item.Code,
                Name = item.Name,
                Presentation = item.Presentation,
                Concentration = item.Concentration,
                DefaultRoute = item.DefaultRoute
            })
            .ToList();

    private sealed record ProcedureCatalogItem(string Code, string Name, string? Description);

    private sealed record MedicationCatalogItem(
        Guid Id, string? Code, string Name, string? Presentation, string? Concentration, MedicationRoute? DefaultRoute);
}
