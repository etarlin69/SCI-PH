using Microsoft.AspNetCore.Authorization;
using SCI_PH.Application.Abstractions.Security;

namespace SCI_PH.Infrastructure.Security
{
    // Resource-based requirement for patient endpoints. Use with the patient id as resource:
    //   await authorization.AuthorizeAsync(User, patientId, PatientRecordRequirement.Read(Permissions.Patients.Read));
    public sealed class PatientRecordRequirement : IAuthorizationRequirement
    {
        private PatientRecordRequirement(string permission, bool isWrite)
        {
            Permission = permission;
            IsWrite = isWrite;
        }

        public string Permission { get; }
        public bool IsWrite { get; }

        public static PatientRecordRequirement Read(string permission) => new(permission, isWrite: false);

        public static PatientRecordRequirement Write(string permission) => new(permission, isWrite: true);
    }

    internal sealed class PatientRecordAuthorizationHandler : AuthorizationHandler<PatientRecordRequirement, Guid>
    {
        private readonly IPatientAccessAuthorizer _authorizer;

        public PatientRecordAuthorizationHandler(IPatientAccessAuthorizer authorizer) => _authorizer = authorizer;

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            PatientRecordRequirement requirement,
            Guid patientId)
        {
            var allowed = requirement.IsWrite
                ? await _authorizer.CanWriteAsync(patientId, requirement.Permission, CancellationToken.None)
                : await _authorizer.CanReadAsync(patientId, requirement.Permission, CancellationToken.None);
            if (allowed)
            {
                context.Succeed(requirement);
            }
        }
    }
}
