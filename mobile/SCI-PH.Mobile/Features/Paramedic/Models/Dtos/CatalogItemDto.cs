namespace SCI_PH.Mobile.Features.Paramedic.Models.Dtos;

/// <summary>Elemento de catálogo (procedimiento o medicamento): código + nombre para mostrar y, si la hay, su descripción.</summary>
public class CatalogItemDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}
