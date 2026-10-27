using SCI_PH.Mobile.Features.Paramedic.Models.Enums;

namespace SCI_PH.Mobile.Features.Paramedic.Models.Dtos;

/// <summary>
/// Medicamento activo del catálogo del servidor (GET /api/mobile/catalogs/medications). Se identifica por Id (el código
/// es opcional). DefaultRoute es solo una sugerencia para preseleccionar la vía.
/// </summary>
public class MedicationCatalogItemDto
{
    public Guid Id { get; set; }
    public string? Code { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Presentation { get; set; }
    public string? Concentration { get; set; }
    public MedicationRoute? DefaultRoute { get; set; }

    /// <summary>Nombre, presentación y concentración, para distinguir entradas con el mismo nombre.</summary>
    public string DisplayName =>
        string.Join(" · ", new[] { Name, Presentation, Concentration }.Where(part => !string.IsNullOrWhiteSpace(part)));
}
