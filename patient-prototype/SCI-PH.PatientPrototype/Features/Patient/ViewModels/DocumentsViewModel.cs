using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using SCI_PH.PatientPrototype.Features.Patient.Models.Dtos;
using SCI_PH.PatientPrototype.Features.Patient.Services.Interfaces;

namespace SCI_PH.PatientPrototype.Features.Patient.ViewModels;

/// <summary>Documentos autorizados. Abrirlos llegará con el Mobile BFF (enlaces temporales, nunca URLs permanentes).</summary>
public sealed partial class DocumentsViewModel : PatientViewModel
{
    private readonly IPatientDataService _data;
    private readonly IPatientDialogService _dialogs;

    public DocumentsViewModel(IPatientDataService data, IPatientDialogService dialogs)
    {
        _data = data;
        _dialogs = dialogs;
    }

    public ObservableCollection<DocumentItemDto> Documents { get; } = [];

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async cancellationToken =>
    {
        var documents = await _data.GetDocumentsAsync(cancellationToken);
        Documents.Clear();
        foreach (var document in documents)
        {
            Documents.Add(document);
        }
    });

    [RelayCommand]
    private Task OpenDocumentAsync(DocumentItemDto document) =>
        _dialogs.ShowAsync(document.Title, "Disponible próximamente.");
}
