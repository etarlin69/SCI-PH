using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Domain.Enums.Encounters;

namespace SCI_PH.Application.Features.Clinical
{
    // See IEncounterAccessService. The assignment lookup lives in the read store, never in ClinicalActor.
    public sealed class EncounterAccessService : IEncounterAccessService
    {
        private readonly IClinicalReadStore _reads;

        public EncounterAccessService(IClinicalReadStore reads) => _reads = reads;

        public async Task EnsureCanAccessAsync(ClinicalActor actor, EncounterAccessTarget encounter, CancellationToken cancellationToken)
        {
            if (encounter.ClinicId is { } clinicId)
            {
                actor.EnsureClinic(clinicId);
                return;
            }

            if (encounter.Type != ClinicalEncounterType.Prehospital
                || !await _reads.HasActiveStaffAssignmentAsync(encounter.EncounterId, actor.StaffId, cancellationToken))
            {
                throw new AccessDeniedException("The current user is not assigned to this prehospital encounter.");
            }
        }
    }
}
