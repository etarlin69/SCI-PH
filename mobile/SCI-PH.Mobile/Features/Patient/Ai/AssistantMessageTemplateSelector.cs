using SCI_PH.Mobile.Features.Patient.Models.Dtos;

namespace SCI_PH.Mobile.Features.Patient.Ai;

/// <summary>Elige la burbuja del paciente o la del asistente según el autor del mensaje.</summary>
public sealed class AssistantMessageTemplateSelector : DataTemplateSelector
{
    public DataTemplate? AssistantTemplate { get; set; }

    public DataTemplate? PatientTemplate { get; set; }

    protected override DataTemplate? OnSelectTemplate(object item, BindableObject container) =>
        item is AssistantMessageDto { IsFromPatient: true } ? PatientTemplate : AssistantTemplate;
}
