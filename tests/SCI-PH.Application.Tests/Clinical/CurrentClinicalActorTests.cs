using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.Features.Clinical;

namespace SCI_PH.Application.Tests.Clinical
{
    public class CurrentClinicalActorTests
    {
        private static readonly CancellationToken None = CancellationToken.None;

        [Fact]
        public async Task UserWithoutClinicalStaffIsDenied()
        {
            var user = FakeCurrentUser.InRole(SecurityRoles.Nurse);

            var error = await Assert.ThrowsAsync<AccessDeniedException>(() => new CurrentClinicalActor(user, new FakeStaffDirectory()).GetAsync(None));
            Assert.Contains("not registered as clinical staff", error.Message);
        }

        [Fact]
        public async Task AdministratorHasNoClinicalBypass()
        {
            var admin = FakeCurrentUser.InRole(SecurityRoles.Administrator);

            await Assert.ThrowsAsync<AccessDeniedException>(() => new CurrentClinicalActor(admin, new FakeStaffDirectory()).GetAsync(None));
        }

        [Fact]
        public async Task InactiveClinicalStaffIsDenied()
        {
            var user = FakeCurrentUser.InRole(SecurityRoles.Doctor);
            var staff = new FakeStaffDirectory();
            staff.Add(user.UserId!.Value, false, Guid.NewGuid());

            var error = await Assert.ThrowsAsync<AccessDeniedException>(() => new CurrentClinicalActor(user, staff).GetAsync(None));
            Assert.Contains("inactive", error.Message);
        }

        [Fact]
        public async Task StaffWithoutActiveAffiliationIsDenied()
        {
            var user = FakeCurrentUser.InRole(SecurityRoles.Doctor);
            var staff = new FakeStaffDirectory();
            staff.Add(user.UserId!.Value, true);

            var error = await Assert.ThrowsAsync<AccessDeniedException>(() => new CurrentClinicalActor(user, staff).GetAsync(None));
            Assert.Contains("no active clinic affiliation", error.Message);
        }

        [Fact]
        public async Task SingleAffiliationIsTheCurrentClinicAutomatically()
        {
            var clinic = Guid.NewGuid();
            var actor = await ActorWithClinicsAsync(clinic);

            Assert.Equal(clinic, actor.ResolveClinic(null));
            Assert.Equal(clinic, actor.ResolveClinic(clinic));
        }

        [Fact]
        public async Task SeveralAffiliationsRequireAClinicId()
        {
            var (first, second) = (Guid.NewGuid(), Guid.NewGuid());
            var actor = await ActorWithClinicsAsync(first, second);

            var error = Assert.Throws<RequestValidationException>(() => actor.ResolveClinic(null));
            Assert.Equal("clinicId", error.Field);
            Assert.Equal(second, actor.ResolveClinic(second));
        }

        [Fact]
        public async Task AForeignClinicIdIsNeverTrusted()
        {
            var actor = await ActorWithClinicsAsync(Guid.NewGuid(), Guid.NewGuid());

            Assert.Throws<AccessDeniedException>(() => actor.ResolveClinic(Guid.NewGuid()));
            Assert.Throws<AccessDeniedException>(() => actor.EnsureClinic(Guid.NewGuid()));
            Assert.Throws<AccessDeniedException>(() => actor.EnsureClinic(null));
        }

        [Fact]
        public async Task ActorIsResolvedOncePerScopeAndCarriesTheAuditIdentity()
        {
            var user = FakeCurrentUser.InRole(SecurityRoles.Nurse);
            var staff = new FakeStaffDirectory();
            var membership = staff.Add(user.UserId!.Value, true, Guid.NewGuid());
            var current = new CurrentClinicalActor(user, staff);

            var actor = await current.GetAsync(None);

            Assert.Same(actor, await current.GetAsync(None));
            Assert.Equal(membership.StaffId, actor.AuditActor.StaffId);
            Assert.Equal(user.UserId, actor.AuditActor.IdentityUserId);
        }

        private static async Task<SCI_PH.Application.Abstractions.Clinical.ClinicalActor> ActorWithClinicsAsync(params Guid[] clinics)
        {
            var user = FakeCurrentUser.InRole(SecurityRoles.Doctor);
            var staff = new FakeStaffDirectory();
            staff.Add(user.UserId!.Value, true, clinics);
            return await new CurrentClinicalActor(user, staff).GetAsync(None);
        }
    }
}
