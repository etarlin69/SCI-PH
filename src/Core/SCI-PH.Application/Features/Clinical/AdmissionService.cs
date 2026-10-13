using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Domain.Entities.Encounters;

namespace SCI_PH.Application.Features.Clinical
{
    // Reception surface (Patients.Identify): find the encounters just opened, including NN ones, without
    // granting Encounters.Read. The projection is administrative only (AdmissionEncounterSummary).
    public sealed class AdmissionService
    {
        private readonly ICurrentClinicalActor _actor;
        private readonly IClinicalReadStore _reads;

        public AdmissionService(ICurrentClinicalActor actor, IClinicalReadStore reads)
        {
            _actor = actor;
            _reads = reads;
        }

        public async Task<IReadOnlyList<AdmissionEncounterSummary>> ListAsync(
            Guid? clinicId, bool onlyUnidentified, CancellationToken cancellationToken)
        {
            var actor = await _actor.GetAsync(cancellationToken);
            var rows = await _reads.ListEncountersAsync(
                actor.ResolveClinic(clinicId), EncounterService.NotOpenStatuses, onlyUnidentified, EncounterService.MaxListSize, cancellationToken);
            return rows.Select(row => row.ToAdmission()).ToList();
        }

        public async Task<AdmissionEncounterSummary> GetAsync(Guid encounterId, CancellationToken cancellationToken)
        {
            var actor = await _actor.GetAsync(cancellationToken);
            var encounter = await _reads.GetEncounterAsync(encounterId, cancellationToken)
                ?? throw new ResourceNotFoundException(nameof(ClinicalEncounter), encounterId);
            actor.EnsureClinic(encounter.ClinicId);
            return encounter.ToAdmission();
        }
    }
}
