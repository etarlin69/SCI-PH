using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.Application.Features.Authorization;

namespace SCI_PH.Application.Tests.Security
{
    public class PatientAccessAuthorizerTests
    {
        private static readonly CancellationToken None = CancellationToken.None;
        private static readonly Guid PatientA = Guid.NewGuid();
        private static readonly Guid PatientB = Guid.NewGuid();
        private static readonly Guid UserA = Guid.NewGuid();

        [Fact]
        public async Task PatientAReadsPatientAButNeverPatientB()
        {
            var authorizer = Create(new FakeCurrentUser(UserA, [SecurityRoles.Patient], []));

            Assert.True(await authorizer.CanReadAsync(PatientA, Permissions.Patients.Read, None));
            Assert.True(await authorizer.CanReadAsync(PatientA, Permissions.Documents.Read, None));
            Assert.False(await authorizer.CanReadAsync(PatientB, Permissions.Patients.Read, None));
        }

        [Fact]
        public async Task PatientNeverWritesNorUsesNonReadablePermissions()
        {
            var authorizer = Create(new FakeCurrentUser(UserA, [SecurityRoles.Patient], []));

            Assert.False(await authorizer.CanWriteAsync(PatientA, Permissions.Patients.Update, None));
            Assert.False(await authorizer.CanReadAsync(PatientA, Permissions.Audit.Read, None));
            Assert.DoesNotContain(Permissions.Patients.Read, RolePermissions.For(SecurityRoles.Patient));
        }

        [Fact]
        public async Task StaffAccessComesFromThePermission()
        {
            var doctor = Create(new FakeCurrentUser(Guid.NewGuid(), [SecurityRoles.Doctor], RolePermissions.For(SecurityRoles.Doctor)));
            var receptionist = Create(new FakeCurrentUser(Guid.NewGuid(), [SecurityRoles.Receptionist], RolePermissions.For(SecurityRoles.Receptionist)));

            Assert.True(await doctor.CanReadAsync(PatientB, Permissions.Encounters.Read, None));
            Assert.True(await doctor.CanWriteAsync(PatientB, Permissions.Prescriptions.Write, None));
            Assert.False(await receptionist.CanReadAsync(PatientB, Permissions.Encounters.Read, None));
        }

        [Fact]
        public async Task AnonymousCallerIsDenied()
        {
            var authorizer = Create(new FakeCurrentUser(null, [], []));

            Assert.False(await authorizer.CanReadAsync(PatientA, Permissions.Patients.Read, None));
            Assert.False(await authorizer.CanWriteAsync(PatientA, Permissions.Patients.Update, None));
        }

        private static PatientAccessAuthorizer Create(ICurrentUser user) => new(user, new FakeLinks());

        private sealed class FakeCurrentUser(Guid? userId, IReadOnlyCollection<string> roles, IReadOnlyCollection<string> permissions) : ICurrentUser
        {
            public bool IsAuthenticated => userId.HasValue;
            public Guid? UserId => userId;
            public IReadOnlyCollection<string> Roles => roles;
            public IReadOnlyCollection<string> Permissions => permissions;
            public bool HasPermission(string permission) => permissions.Contains(permission);
        }

        // Patient A is linked to User A; Patient B to nobody known.
        private sealed class FakeLinks : IClinicalIdentityLinkService
        {
            public Task<ClinicalSubject?> FindSubjectAsync(InvitationSubjectType type, Guid subjectId, CancellationToken cancellationToken) =>
                Task.FromResult<ClinicalSubject?>(null);

            public Task<bool> LinkAsync(InvitationSubjectType type, Guid subjectId, Guid userId, CancellationToken cancellationToken) =>
                Task.FromResult(false);

            public Task<Guid?> FindPatientIdByUserAsync(Guid userId, CancellationToken cancellationToken) =>
                Task.FromResult<Guid?>(userId == UserA ? PatientA : null);
        }
    }
}
