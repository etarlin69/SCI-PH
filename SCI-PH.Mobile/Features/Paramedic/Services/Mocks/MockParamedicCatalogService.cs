using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;
using SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;

namespace SCI_PH.Mobile.Features.Paramedic.Services.Mocks;

/// <summary>Catálogo SIMULADO solo para desarrollo; no representa el catálogo clínico real.</summary>
public class MockParamedicCatalogService : IParamedicCatalogService
{
    private const int SimulatedDelayMs = 300;

    public async Task<IReadOnlyList<CatalogItemDto>> GetProceduresAsync()
    {
        await Task.Delay(SimulatedDelayMs);
        return new List<CatalogItemDto>
        {
            new() { Code = "PROC-001", Name = "Inmovilización" },
            new() { Code = "PROC-002", Name = "Control de hemorragia" },
            new() { Code = "PROC-003", Name = "Oxigenoterapia" },
            new() { Code = "PROC-004", Name = "Acceso venoso periférico" },
            new() { Code = "PROC-005", Name = "Reanimación cardiopulmonar" }
        };
    }

    public async Task<IReadOnlyList<CatalogItemDto>> GetMedicationsAsync()
    {
        await Task.Delay(SimulatedDelayMs);
        return new List<CatalogItemDto>
        {
            new() { Code = "MED-001", Name = "Medicamento A (simulado)" },
            new() { Code = "MED-002", Name = "Medicamento B (simulado)" },
            new() { Code = "MED-003", Name = "Medicamento C (simulado)" }
        };
    }
}
