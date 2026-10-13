using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCI_PH.PatientPrototype.Features.Patient.Models.Dtos;
using SCI_PH.PatientPrototype.Features.Patient.Services.Interfaces;

namespace SCI_PH.PatientPrototype.Features.Patient.ViewModels;

/// <summary>Conversación con el asistente educativo SCI-PH AI.</summary>
public sealed partial class AiAssistantViewModel : PatientViewModel
{
    public const string Greeting = "Hola. Puedo ayudarte a comprender términos médicos y resultados disponibles en tu expediente.";

    private readonly IPatientAssistantService _assistant;

    public AiAssistantViewModel(IPatientAssistantService assistant)
    {
        _assistant = assistant;
        Messages.Add(new AssistantMessageDto(Greeting, IsFromPatient: false));
    }

    public ObservableCollection<AssistantMessageDto> Messages { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string? question;

    private bool CanSend() => !string.IsNullOrWhiteSpace(Question);

    [RelayCommand(CanExecute = nameof(CanSend))]
    private Task SendAsync() => RunAsync(async cancellationToken =>
    {
        string text = Question?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return;
        }

        Messages.Add(new AssistantMessageDto(text, IsFromPatient: true));
        Question = string.Empty;
        string answer = await _assistant.AskAsync(text, cancellationToken);
        Messages.Add(new AssistantMessageDto(answer, IsFromPatient: false));
    });
}
