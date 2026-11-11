using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Enums.Encounters;

namespace SCI_PH.Application.Abstractions.Clinical
{
    // Clinical results (laboratory, imaging, ...) of an encounter. Shares the unit of work of IClinicalWriteStore: what it
    // tracks (the audit entry, the encounter's concurrency tokens) is saved with the result.
    public interface IClinicalResultStore
    {
        // Newest first: resulted ones by their time, then the pending orders.
        Task<IReadOnlyList<ClinicalResultSummary>> ListForEncounterAsync(Guid encounterId, CancellationToken cancellationToken);

        // The result of THAT encounter, not tracked (its change is saved by SaveTransitionAsync); null otherwise.
        Task<ClinicalResult?> FindForEncounterAsync(Guid encounterId, Guid resultId, CancellationToken cancellationToken);

        void Add(ClinicalResult result);

        // One transaction: the result's new state is written only if its stored status is still expectedStatus
        // (UPDATE ... WHERE "Status" = expected), together with everything IClinicalWriteStore tracks (audit entry, encounter
        // check). A result changed by another request in between is a ResourceConflictException and nothing is written.
        Task SaveTransitionAsync(ClinicalResult result, ClinicalResultStatus expectedStatus, CancellationToken cancellationToken);
    }
}
