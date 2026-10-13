using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using SCI_PH.Mobile.Common.Dialogs;
using SCI_PH.Mobile.Common.ViewModels;
using SCI_PH.Mobile.Features.Patient.Models.Dtos;
using SCI_PH.Mobile.Features.Patient.Services.Interfaces;

namespace SCI_PH.Mobile.Features.Patient.ViewModels;

/// <summary>Documentos autorizados. Abrirlos llegará con el Mobile BFF (enlaces temporales, nunca URLs permanentes).</summary>
public sealed partial class DocumentsViewModel : BaseViewModel
{
    private readonly IPatientDataService _data;
    private readonly IAppDialogService _dialogs;

    public DocumentsViewModel(IPatientDataService data, IAppDialogService dialogs)
    {
        _data = data;
        _dialogs = dialogs;
    }

    public ObservableCollection<DocumentItemDto> Documents { get; } = [];

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () =>
    {
        var documents = await _data.GetDocumentsAsync();
        Documents.Clear();
        foreach (var document in documents)
        {
            Documents.Add(document);
        }
    }, GenericErrorMessage);

    [RelayCommand]
    private Task OpenDocumentAsync(DocumentItemDto document) =>
        _dialogs.ShowAsync(document.Title, "Disponible próximamente.");
}
