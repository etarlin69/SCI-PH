using SCI_PH.Application.DTOs.Documents;

namespace SCI_PH.Application.Abstractions.Documents
{
    // The Document Service of the approved storage design: it validates and authorizes, has the Storage Service keep the
    // content and writes the metadata (PostgreSQL); a read goes metadata -> blob path -> Storage Service. Implemented by
    // ClinicalDocumentService.
    public interface IDocumentService
    {
        Task<IReadOnlyList<ClinicalDocumentSummary>> ListForEncounterAsync(Guid encounterId, CancellationToken cancellationToken);

        Task<UploadClinicalDocumentResult> UploadToEncounterAsync(
            Guid encounterId, UploadClinicalDocumentRequest request, CancellationToken cancellationToken);

        Task<ClinicalDocumentContent> OpenFromEncounterAsync(Guid encounterId, Guid documentId, CancellationToken cancellationToken);
    }

    // The clinical documents of the ClinicalDbContext. Shares the unit of work of IClinicalWriteStore: its SaveChangesAsync
    // commits them together with the audit entry and the idempotency receipt.
    public interface IClinicalDocumentStore
    {
        Task<IReadOnlyList<ClinicalDocumentSummary>> ListAvailableForEncounterAsync(Guid encounterId, CancellationToken cancellationToken);

        // The document of that encounter (no tracking), in any status; null if it does not exist or is of another one.
        Task<Domain.Entities.Documents.ClinicalDocument?> FindForEncounterAsync(Guid encounterId, Guid documentId, CancellationToken cancellationToken);

        void Add(Domain.Entities.Documents.ClinicalDocument document);
    }
}
