using Microsoft.EntityFrameworkCore;
using SCI_PH.Application.Abstractions.Documents;
using SCI_PH.Application.DTOs.Documents;
using SCI_PH.Domain.Entities.Documents;
using SCI_PH.Domain.Enums.Documents;
using SCI_PH.Persistence.Contexts;

namespace SCI_PH.Persistence.Clinical.Services
{
    internal sealed class ClinicalDocumentStore : IClinicalDocumentStore
    {
        private readonly ClinicalDbContext _clinical;

        public ClinicalDocumentStore(ClinicalDbContext clinical) => _clinical = clinical;

        // Newest first; only what can be downloaded (never a pending, rejected or deleted one).
        public async Task<IReadOnlyList<ClinicalDocumentSummary>> ListAvailableForEncounterAsync(Guid encounterId, CancellationToken cancellationToken) =>
            await _clinical.ClinicalDocuments.AsNoTracking()
                .Where(d => d.ClinicalEncounterId == encounterId && d.Status == ClinicalDocumentStatus.Available)
                .OrderByDescending(d => d.UploadedAtUtc).ThenBy(d => d.Id)
                .Select(d => new ClinicalDocumentSummary(d.Id, encounterId, d.Type, d.FileName, d.ContentType, d.SizeBytes, d.UploadedAtUtc))
                .ToListAsync(cancellationToken);

        public Task<ClinicalDocument?> FindForEncounterAsync(Guid encounterId, Guid documentId, CancellationToken cancellationToken) =>
            _clinical.ClinicalDocuments.AsNoTracking()
                .SingleOrDefaultAsync(d => d.Id == documentId && d.ClinicalEncounterId == encounterId, cancellationToken);

        public void Add(ClinicalDocument document) => _clinical.ClinicalDocuments.Add(document);
    }
}
