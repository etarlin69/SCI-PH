using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Errors;

namespace SCI_PH.Application.Features.Clinical
{
    // Applies the clinical actor rules; IClinicalStaffDirectory (Persistence) does the lookup. Scoped: resolved once per request.
    public sealed class CurrentClinicalActor : ICurrentClinicalActor
    {
        private readonly ICurrentUser _currentUser;
        private readonly IClinicalStaffDirectory _staff;
        private ClinicalActor? _actor;

        public CurrentClinicalActor(ICurrentUser currentUser, IClinicalStaffDirectory staff)
        {
            _currentUser = currentUser;
            _staff = staff;
        }

        public async Task<ClinicalActor> GetAsync(CancellationToken cancellationToken)
        {
            if (_actor is not null)
            {
                return _actor;
            }

            if (!_currentUser.IsAuthenticated || _currentUser.UserId is not { } userId)
            {
                throw new AccessDeniedException("An authenticated user is required.");
            }

            var membership = await _staff.FindByIdentityUserAsync(userId, cancellationToken)
                ?? throw new AccessDeniedException("The current user is not registered as clinical staff.");
            if (!membership.IsActive)
            {
                throw new AccessDeniedException("The clinical staff record of the current user is inactive.");
            }

            if (membership.ActiveClinicIds.Count == 0)
            {
                throw new AccessDeniedException("The current user has no active clinic affiliation.");
            }

            return _actor = new ClinicalActor(userId, membership.StaffId, membership.ActiveClinicIds);
        }
    }
}
