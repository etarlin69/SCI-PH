using SCI_PH.Domain.Entities.Auditing;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Entities.Patients;

namespace SCI_PH.Application.Abstractions.Clinical
{
    // Tracked Domain aggregates for one use case. Everything added or changed is written by a single
    // SaveChangesAsync, which is one database transaction: a clinical record, the encounter transition and its
    // ClinicalAuditEntry are committed together or not at all.
    public interface IClinicalWriteStore
    {
        Task<ClinicalEncounter?> FindEncounterAsync(Guid encounterId, CancellationToken cancellationToken);

        void Add(Patient patient);

        void Add(ClinicalEncounter encounter);

        void Add(Triage triage);

        void Add(VitalSign vitalSign);

        void Add(ClinicalAuditEntry auditEntry);

        // Concurrent changes and known uniqueness violations surface as ResourceConflictException.
        Task SaveChangesAsync(CancellationToken cancellationToken);
    }
}
