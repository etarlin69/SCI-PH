using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;
using SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;
using SCI_PH.Mobile.Features.Paramedic.Services.Mocks;

namespace SCI_PH.Mobile.Features.Paramedic.Services;

/// <summary>
/// Catálogos del área paramédica. REAL: procedimientos, el catálogo fijo del servidor (GET /api/mobile/catalogs/procedures).
/// Sigue en MOCK: medicamentos. Los fallos de la llamada real llegan como <see cref="MobileApiException"/>.
/// </summary>
public sealed class HttpParamedicCatalogService : IParamedicCatalogService
{
    private const string ProceduresCatalog = "api/mobile/catalogs/procedures";

    private readonly IMobileApiClient _client;
    private readonly MockParamedicCatalogService _mock;

    public HttpParamedicCatalogService(IMobileApiClient client, MockParamedicCatalogService mock)
    {
        _client = client;
        _mock = mock;
    }

    public async Task<IReadOnlyList<CatalogItemDto>> GetProceduresAsync() =>
        (await _client.GetAsync<List<ProcedureCatalogItem>>(ProceduresCatalog))
            .Select(item => new CatalogItemDto { Code = item.Code, Name = item.Name, Description = item.Description })
            .ToList();

    public Task<IReadOnlyList<CatalogItemDto>> GetMedicationsAsync() => _mock.GetMedicationsAsync();

    private sealed record ProcedureCatalogItem(string Code, string Name, string? Description);
}
