using SCI_PH.Domain.Enums.Encounters;

namespace SCI_PH.Application.Abstractions.Clinical
{
    // Access of the clinical actor to one concrete encounter:
    // - with a clinic: the actor must be affiliated with it (ClinicalActor.EnsureClinic, unchanged);
    // - Prehospital without a clinic (care before arriving at a hospital): the actor's ClinicalStaff must have an
    //   active EncounterStaffAssignment on that encounter; the paramedic's base clinic never grants access;
    // - any other encounter without a clinic: never accessible.
    // Permissions (and Emergencies.Operate for the module) are checked by the presentation layer; patient-level rules
    // by IPatientAccessAuthorizer. Throws AccessDeniedException.
    public interface IEncounterAccessService
    {
        Task EnsureCanAccessAsync(ClinicalActor actor, EncounterAccessTarget encounter, CancellationToken cancellationToken);
    }

    public sealed record EncounterAccessTarget(Guid EncounterId, ClinicalEncounterType Type, Guid? ClinicId);
}
