using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Security;

namespace SCI_PH.Application.Features.Authorization
{
    // Staff: access requires the permission. Patient accounts: read-only access to their own record only,
    // resolved through Patient.IdentityUserId; a patient id supplied by the client is never trusted by itself.
    public sealed class PatientAccessAuthorizer : IPatientAccessAuthorizer
    {
        // Permissions a patient may exercise on their own record.
        public static IReadOnlyCollection<string> SelfReadablePermissions { get; } =
        [
            Permissions.Patients.Read,
            Permissions.Encounters.Read,
            Permissions.Medications.Read,
            Permissions.Documents.Read,
            Permissions.Transfers.Read
        ];

        private readonly ICurrentUser _currentUser;
        private readonly IClinicalIdentityLinkService _clinicalLinks;

        public PatientAccessAuthorizer(ICurrentUser currentUser, IClinicalIdentityLinkService clinicalLinks)
        {
            _currentUser = currentUser;
            _clinicalLinks = clinicalLinks;
        }

        public async Task<bool> CanReadAsync(Guid patientId, string permission, CancellationToken cancellationToken)
        {
            if (!_currentUser.IsAuthenticated)
            {
                return false;
            }

            if (_currentUser.HasPermission(permission))
            {
                return true;
            }

            return _currentUser.Roles.Contains(SecurityRoles.Patient)
                && SelfReadablePermissions.Contains(permission)
                && _currentUser.UserId is { } userId
                && await _clinicalLinks.FindPatientIdByUserAsync(userId, cancellationToken) == patientId;
        }

        // Patients never write clinical data through their account.
        public Task<bool> CanWriteAsync(Guid patientId, string permission, CancellationToken cancellationToken) =>
            Task.FromResult(_currentUser.IsAuthenticated && _currentUser.HasPermission(permission));
    }
}
