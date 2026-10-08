using SCI_PH.Application.Common.Errors;
using SCI_PH.Domain.ValueObjects;

namespace SCI_PH.Application.Abstractions.Clinical
{
    // The authenticated user acting as a clinical professional: ICurrentUser -> ClinicalStaff.IdentityUserId ->
    // active ClinicStaffAffiliation -> Clinic. There is no role bypass: an Administrator acts clinically only
    // when they are also an active ClinicalStaff with an active affiliation.
    public interface ICurrentClinicalActor
    {
        // Throws AccessDeniedException when the caller is not an active ClinicalStaff with at least one active clinic.
        Task<ClinicalActor> GetAsync(CancellationToken cancellationToken);
    }

    public sealed class ClinicalActor
    {
        public ClinicalActor(Guid identityUserId, Guid staffId, IReadOnlyCollection<Guid> activeClinicIds)
        {
            IdentityUserId = identityUserId;
            StaffId = staffId;
            ActiveClinicIds = activeClinicIds;
        }

        public Guid IdentityUserId { get; }
        public Guid StaffId { get; }
        public IReadOnlyCollection<Guid> ActiveClinicIds { get; }

        public AuditActor AuditActor => AuditActor.User(IdentityUserId, StaffId);

        // Clinic for an operation that needs one. A requested id is never trusted by itself: it must be one of
        // the active affiliations. Without a request, a single affiliation is used automatically.
        public Guid ResolveClinic(Guid? requestedClinicId)
        {
            if (requestedClinicId is { } requested)
            {
                return ActiveClinicIds.Contains(requested)
                    ? requested
                    : throw new AccessDeniedException("The user is not affiliated with the requested clinic.");
            }

            return ActiveClinicIds.Count == 1
                ? ActiveClinicIds.First()
                : throw new RequestValidationException(
                    "clinicId is required because the user is affiliated with several clinics.", "clinicId");
        }

        // Access to an existing resource of a clinic (prehospital encounters without clinic are not visible here).
        public void EnsureClinic(Guid? clinicId)
        {
            if (clinicId is not { } id || !ActiveClinicIds.Contains(id))
            {
                throw new AccessDeniedException("The resource belongs to a clinic the user is not affiliated with.");
            }
        }
    }
}
