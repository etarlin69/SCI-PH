using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.Application.Features.Accounts;
using SCI_PH.Domain.Enums.Organizations;
using SCI_PH.IntegrationTests.Security.Support;
using SCI_PH.Persistence.Identity.Models;

namespace SCI_PH.IntegrationTests.Security
{
    public class InvitationTests
    {
        private static readonly CancellationToken None = CancellationToken.None;
        private static readonly Guid Actor = Guid.NewGuid();

        [Fact]
        public async Task PatientInvitationCreatesALinkedPatientAccount()
        {
            await using var context = new SecurityTestContext();
            await context.SeedAsync();
            var email = $"patient-{Guid.NewGuid():N}@sci-ph.test";
            var patientId = await context.CreatePatientAsync(email);

            var invitation = await Invitations(context, s => s.InvitePatientAsync(patientId, Actor, ClientContext.Unknown, None));
            var token = TokenFrom(context, email);
            var password = TestSettings.NewPassword();
            var activation = await Invitations(context, s => s.ActivateAsync(new ActivateAccountRequest(token, password), ClientContext.Unknown, None));

            Assert.Equal(InvitationOutcome.Created, invitation.Outcome);
            Assert.True(invitation.Invitation!.EmailSent);
            Assert.Equal(SecurityRoles.Patient, invitation.Invitation.IntendedRole);
            Assert.True(activation.Succeeded);
            var login = await context.LoginAsync(email, password);
            Assert.NotNull(login);
            Assert.Equal([SecurityRoles.Patient], login.User.Roles);
            Assert.Empty(login.User.Permissions);
            Assert.Equal(login.User.Id, await context.ClinicalAsync(db => db.Patients.Where(p => p.Id == patientId).Select(p => p.IdentityUserId).SingleAsync()));
            Assert.True(await context.DbAsync(db => db.Users.Where(u => u.Id == login.User.Id).Select(u => u.EmailConfirmed).SingleAsync()));
            Assert.NotEqual(token, await context.DbAsync(db => db.AccountInvitations.Select(i => i.TokenHash).SingleAsync()));
        }

        [Fact]
        public async Task StaffInvitationGrantsOnlyTheInvitedStaffRole()
        {
            await using var context = new SecurityTestContext();
            await context.SeedAsync();
            var staffId = await context.CreateStaffAsync(ClinicalStaffType.Physician);
            var email = $"doctor-{Guid.NewGuid():N}@sci-ph.test";

            var administrator = await InviteStaff(context, staffId, email, SecurityRoles.Administrator);
            var patientRole = await InviteStaff(context, staffId, email, SecurityRoles.Patient);
            var doctor = await InviteStaff(context, staffId, email, SecurityRoles.Doctor);
            var password = TestSettings.NewPassword();
            await Invitations(context, s => s.ActivateAsync(new ActivateAccountRequest(TokenFrom(context, email), password), ClientContext.Unknown, None));

            Assert.Equal(InvitationOutcome.InvalidRole, administrator.Outcome);
            Assert.Equal(InvitationOutcome.InvalidRole, patientRole.Outcome);
            Assert.Equal(InvitationOutcome.Created, doctor.Outcome);
            var login = (await context.LoginAsync(email, password))!;
            Assert.Equal([SecurityRoles.Doctor], login.User.Roles);
            Assert.Equal(login.User.Id, await context.ClinicalAsync(db => db.ClinicalStaffMembers.Where(s => s.Id == staffId).Select(s => s.IdentityUserId).SingleAsync()));
        }

        [Fact]
        public async Task ActivationRequestCannotCarrySubjectOrRole()
        {
            Assert.Equal(["Token", "Password"], typeof(ActivateAccountRequest).GetProperties().Select(p => p.Name));
            await Task.CompletedTask;
        }

        [Fact]
        public async Task PatientATokenNeverActivatesPatientB()
        {
            await using var context = new SecurityTestContext();
            await context.SeedAsync();
            var emailA = $"a-{Guid.NewGuid():N}@sci-ph.test";
            var emailB = $"b-{Guid.NewGuid():N}@sci-ph.test";
            var patientA = await context.CreatePatientAsync(emailA);
            var patientB = await context.CreatePatientAsync(emailB);
            await Invitations(context, s => s.InvitePatientAsync(patientA, Actor, ClientContext.Unknown, None));
            await Invitations(context, s => s.InvitePatientAsync(patientB, Actor, ClientContext.Unknown, None));

            await Invitations(context, s => s.ActivateAsync(new ActivateAccountRequest(TokenFrom(context, emailA), TestSettings.NewPassword()), ClientContext.Unknown, None));

            Assert.NotNull(await context.ClinicalAsync(db => db.Patients.Where(p => p.Id == patientA).Select(p => p.IdentityUserId).SingleAsync()));
            Assert.Null(await context.ClinicalAsync(db => db.Patients.Where(p => p.Id == patientB).Select(p => p.IdentityUserId).SingleAsync()));
        }

        [Fact]
        public async Task InvitationIsSingleUse()
        {
            await using var context = new SecurityTestContext();
            var (email, token) = await InvitePatientAsync(context);

            var first = await Activate(context, token);
            var second = await Activate(context, token);

            Assert.True(first.Succeeded);
            Assert.False(second.Succeeded);
            Assert.Empty(second.Errors);
            Assert.Equal(1, await context.DbAsync(db => db.Users.CountAsync(u => u.Email == email)));
        }

        [Fact]
        public async Task ExpiredRevokedAndReplacedInvitationsAreRejected()
        {
            await using var context = new SecurityTestContext();
            var (_, expiredToken) = await InvitePatientAsync(context);
            context.Time.Advance(TimeSpan.FromHours(25));
            Assert.False((await Activate(context, expiredToken)).Succeeded);

            var (_, revokedToken) = await InvitePatientAsync(context);
            var revokedId = await context.DbAsync(db => db.AccountInvitations.OrderByDescending(i => i.CreatedAtUtc).Select(i => i.Id).FirstAsync());
            Assert.True(await Invitations(context, s => s.RevokeAsync(revokedId, Actor, ClientContext.Unknown, None)));
            Assert.False((await Activate(context, revokedToken)).Succeeded);

            var patientId = await context.CreatePatientAsync($"again-{Guid.NewGuid():N}@sci-ph.test");
            await Invitations(context, s => s.InvitePatientAsync(patientId, Actor, ClientContext.Unknown, None));
            var replacedToken = FakeEmailSender.LinkParameter(context.Email.Sent[^1], "token");
            await Invitations(context, s => s.InvitePatientAsync(patientId, Actor, ClientContext.Unknown, None));
            var currentToken = FakeEmailSender.LinkParameter(context.Email.Sent[^1], "token");
            Assert.False((await Activate(context, replacedToken)).Succeeded);
            Assert.True((await Activate(context, currentToken)).Succeeded);
        }

        [Fact]
        public async Task RejectedPasswordKeepsTheInvitationUsable()
        {
            await using var context = new SecurityTestContext();
            var (_, token) = await InvitePatientAsync(context);

            var weak = await Activate(context, token, "weak");
            var strong = await Activate(context, token);

            Assert.False(weak.Succeeded);
            Assert.NotEmpty(weak.Errors);
            Assert.True(strong.Succeeded);
        }

        [Fact]
        public async Task SubjectRulesAreChecked()
        {
            await using var context = new SecurityTestContext();
            await context.SeedAsync();
            var withoutEmail = await context.CreatePatientAsync();
            var existing = await context.CreateUserAsync(SecurityRoles.Nurse);
            var staffId = await context.CreateStaffAsync();

            Assert.Equal(InvitationOutcome.SubjectHasNoEmail,
                (await Invitations(context, s => s.InvitePatientAsync(withoutEmail, Actor, ClientContext.Unknown, None))).Outcome);
            Assert.Equal(InvitationOutcome.SubjectNotFound,
                (await Invitations(context, s => s.InvitePatientAsync(Guid.NewGuid(), Actor, ClientContext.Unknown, None))).Outcome);
            Assert.Equal(InvitationOutcome.EmailInUse,
                (await InviteStaff(context, staffId, existing.Email, SecurityRoles.Nurse)).Outcome);

            var (_, token) = await InvitePatientAsync(context);
            await Activate(context, token);
            var linkedPatient = await context.ClinicalAsync(db => db.Patients.Where(p => p.IdentityUserId != null).Select(p => p.Id).FirstAsync());
            Assert.Equal(InvitationOutcome.SubjectAlreadyLinked,
                (await Invitations(context, s => s.InvitePatientAsync(linkedPatient, Actor, ClientContext.Unknown, None))).Outcome);
        }

        [Fact]
        public async Task FailedDeliveryIsReportedAndTheInvitationCanBeReissued()
        {
            await using var context = new SecurityTestContext();
            await context.SeedAsync();
            var email = $"p-{Guid.NewGuid():N}@sci-ph.test";
            var patientId = await context.CreatePatientAsync(email);
            context.Email.Fail = true;

            var failed = await Invitations(context, s => s.InvitePatientAsync(patientId, Actor, ClientContext.Unknown, None));
            context.Email.Fail = false;
            var reissued = await Invitations(context, s => s.ReissueAsync(failed.Invitation!.Id, Actor, ClientContext.Unknown, None));

            Assert.False(failed.Invitation!.EmailSent);
            Assert.True(reissued.Invitation!.EmailSent);
            Assert.True((await Activate(context, TokenFrom(context, email))).Succeeded);
        }

        [Fact]
        public async Task ActivatedAccountStartsInactiveUntilLinkedAndUnusedAccountsAreCleanedUp()
        {
            await using var context = new SecurityTestContext();
            await context.SeedAsync();

            var creation = await context.RunAsync(sp => sp.GetRequiredService<SCI_PH.Application.Abstractions.Security.IUserDirectory>()
                .CreateInvitedAccountAsync($"pending-{Guid.NewGuid():N}@sci-ph.test", TestSettings.NewPassword(), SecurityRoles.Patient, None));
            var user = await context.DbAsync(db => db.Users.SingleAsync(u => u.Id == creation.UserId));

            Assert.False(user.IsActive);
            Assert.True(user.EmailConfirmed);
            await context.RunAsync(sp => sp.GetRequiredService<SCI_PH.Application.Abstractions.Security.IUserDirectory>().DeleteUnusedAccountAsync(user.Id, None));
            Assert.False(await context.DbAsync(db => db.Users.AnyAsync(u => u.Id == user.Id)));
        }

        private static Task<T> Invitations<T>(SecurityTestContext context, Func<AccountInvitationService, Task<T>> action) => context.With(action);

        private static Task<InvitationResult> InviteStaff(SecurityTestContext context, Guid staffId, string email, string role) =>
            Invitations(context, s => s.InviteStaffAsync(new InviteStaffRequest(staffId, email, role), Actor, ClientContext.Unknown, None));

        private static Task<AccountActivationResult> Activate(SecurityTestContext context, string token, string? password = null) =>
            Invitations(context, s => s.ActivateAsync(new ActivateAccountRequest(token, password ?? TestSettings.NewPassword()), ClientContext.Unknown, None));

        private static async Task<(string Email, string Token)> InvitePatientAsync(SecurityTestContext context)
        {
            await context.SeedAsync();
            var email = $"patient-{Guid.NewGuid():N}@sci-ph.test";
            var patientId = await context.CreatePatientAsync(email);
            await Invitations(context, s => s.InvitePatientAsync(patientId, Actor, ClientContext.Unknown, None));
            return (email, TokenFrom(context, email));
        }

        private static string TokenFrom(SecurityTestContext context, string email) =>
            FakeEmailSender.LinkParameter(context.Email.LastTo(email), "token");
    }
}
