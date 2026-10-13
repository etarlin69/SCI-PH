using SCI_PH.Domain.Entities.Auditing;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Entities.Medications;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Entities.Transport;

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

        void Add(ClinicalNote note);

        void Add(Diagnosis diagnosis);

        // With its details (one aggregate).
        void Add(Prescription prescription);

        void Add(MedicationAdministration administration);

        Task<ClinicalTransfer?> FindTransferAsync(Guid transferId, CancellationToken cancellationToken);

        void Add(ClinicalTransfer transfer);

        void Add(ClinicalAuditEntry auditEntry);

        // A clinical record only inserts rows, so the encounter's concurrency tokens (Status, PatientId) would not be
        // checked. This makes the next SaveChangesAsync re-check them: if another transaction closed or changed the
        // encounter after it was loaded, the record is rejected (ResourceConflictException) instead of being written.
        void EnsureEncounterUnchanged(ClinicalEncounter encounter);

        // Concurrent changes and known uniqueness violations surface as ResourceConflictException.
        Task SaveChangesAsync(CancellationToken cancellationToken);
    }
}
