using Microsoft.Extensions.Options;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;

namespace SCI_PH.Application.Features.Accounts
{
    // Controlled account creation: there is no public registration. An authorized actor invites an existing
    // Patient or ClinicalStaff record; the invitee only presents the one-time token, so subject, email and
    // role always come from the stored invitation and cannot be chosen by the public caller.
    public sealed class AccountInvitationService
    {
        private const int TokenBytes = 32;

        private readonly IAccountInvitationStore _invitations;
        private readonly IClinicalIdentityLinkService _clinicalLinks;
        private readonly IUserDirectory _users;
        private readonly IIdentityService _identity;
        private readonly ISecureTokenGenerator _secrets;
        private readonly IEmailSender _email;
        private readonly ISecurityAuditLog _audit;
        private readonly TimeProvider _time;
        private readonly AccountEmailOptions _options;

        public AccountInvitationService(
            IAccountInvitationStore invitations,
            IClinicalIdentityLinkService clinicalLinks,
            IUserDirectory users,
            IIdentityService identity,
            ISecureTokenGenerator secrets,
            IEmailSender email,
            ISecurityAuditLog audit,
            TimeProvider time,
            IOptions<AccountEmailOptions> options)
        {
            _invitations = invitations;
            _clinicalLinks = clinicalLinks;
            _users = users;
            _identity = identity;
            _secrets = secrets;
            _email = email;
            _audit = audit;
            _time = time;
            _options = options.Value;
        }

        // The email is the patient's primary active Email identifier, never a value sent by the caller.
        public async Task<InvitationResult> InvitePatientAsync(Guid patientId, Guid actorUserId, ClientContext client, CancellationToken cancellationToken)
        {
            var subject = await _clinicalLinks.FindSubjectAsync(InvitationSubjectType.Patient, patientId, cancellationToken);
            var problem = CheckSubject(subject);
            if (problem is not null)
            {
                return InvitationResult.Failed(problem.Value);
            }

            return subject!.ContactEmail is null
                ? InvitationResult.Failed(InvitationOutcome.SubjectHasNoEmail)
                : await CreateAsync(subject, subject.ContactEmail, SecurityRoles.Patient, actorUserId, client, cancellationToken);
        }

        public async Task<InvitationResult> InviteStaffAsync(InviteStaffRequest request, Guid actorUserId, ClientContext client, CancellationToken cancellationToken)
        {
            if (!SecurityRoles.InvitableStaff.Contains(request.Role))
            {
                return InvitationResult.Failed(InvitationOutcome.InvalidRole);
            }

            var subject = await _clinicalLinks.FindSubjectAsync(InvitationSubjectType.ClinicalStaff, request.ClinicalStaffId, cancellationToken);
            var problem = CheckSubject(subject);
            return problem is not null
                ? InvitationResult.Failed(problem.Value)
                : await CreateAsync(subject!, request.Email.Trim(), request.Role, actorUserId, client, cancellationToken);
        }

        // Replaces the token: the previous invitation is revoked and a new link is sent.
        public async Task<InvitationResult> ReissueAsync(Guid invitationId, Guid actorUserId, ClientContext client, CancellationToken cancellationToken)
        {
            var invitation = await _invitations.FindAsync(invitationId, cancellationToken);
            if (invitation is null)
            {
                return InvitationResult.Failed(InvitationOutcome.InvitationNotFound);
            }

            if (invitation.Status == AccountInvitationStatus.Accepted)
            {
                return InvitationResult.Failed(InvitationOutcome.InvitationNotPending);
            }

            var subject = await _clinicalLinks.FindSubjectAsync(invitation.SubjectType, invitation.SubjectId, cancellationToken);
            var problem = CheckSubject(subject);
            return problem is not null
                ? InvitationResult.Failed(problem.Value)
                : await CreateAsync(subject!, invitation.Email, invitation.IntendedRole, actorUserId, client, cancellationToken);
        }

        public async Task<bool> RevokeAsync(Guid invitationId, Guid actorUserId, ClientContext client, CancellationToken cancellationToken)
        {
            if (!await _invitations.RevokeAsync(invitationId, cancellationToken))
            {
                return false;
            }

            await _audit.RecordAsync(SecurityAuditRecord.Success(SecurityAuditEventType.AccountInvitationRevoked, null, client) with
            {
                ActorUserId = actorUserId
            }, cancellationToken);
            return true;
        }

        // Claim -> create inactive account -> link clinical record -> activate -> complete.
        // Any failure before activation compensates, so no usable account exists without its clinical link.
        public async Task<AccountActivationResult> ActivateAsync(ActivateAccountRequest request, ClientContext client, CancellationToken cancellationToken)
        {
            var invitation = await _invitations.FindByTokenHashAsync(_secrets.Hash(request.Token), cancellationToken);
            var rejection = await CheckInvitationAsync(invitation, cancellationToken);
            if (rejection is not null)
            {
                return await RejectAsync(rejection.Value, client, cancellationToken);
            }

            if (!await _invitations.TryClaimAsync(invitation!.Id, cancellationToken))
            {
                return await RejectAsync(SecurityFailureReason.InvitationNotPending, client, cancellationToken);
            }

            Guid? createdUserId = null;
            var linked = false;
            try
            {
                var account = await _users.CreateInvitedAccountAsync(invitation.Email, request.Password, invitation.IntendedRole, cancellationToken);
                if (!account.Succeeded)
                {
                    await _invitations.ReleaseAsync(invitation.Id, cancellationToken);
                    var reason = account.EmailInUse ? SecurityFailureReason.EmailInUse : SecurityFailureReason.PasswordPolicy;
                    await RecordActivationFailureAsync(reason, client, cancellationToken);
                    return account.EmailInUse ? AccountActivationResult.InvalidInvitation : AccountActivationResult.PasswordRejected(account.Errors);
                }

                createdUserId = account.UserId!.Value;
                linked = await _clinicalLinks.LinkAsync(invitation.SubjectType, invitation.SubjectId, createdUserId.Value, cancellationToken);
                if (!linked)
                {
                    await _users.DeleteUnusedAccountAsync(createdUserId.Value, cancellationToken);
                    await _invitations.RevokeAsync(invitation.Id, cancellationToken);
                    return await RejectAsync(SecurityFailureReason.SubjectUnavailable, client, cancellationToken);
                }

                await _identity.SetActiveAsync(createdUserId.Value, isActive: true, cancellationToken);
                await _invitations.CompleteAsync(invitation.Id, createdUserId.Value, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException && !linked)
            {
                // Unexpected failure before the clinical link: undo so the invitation can be used again.
                if (createdUserId.HasValue)
                {
                    await _users.DeleteUnusedAccountAsync(createdUserId.Value, CancellationToken.None);
                }

                await _invitations.ReleaseAsync(invitation.Id, CancellationToken.None);
                throw;
            }

            // A failure after linking leaves an inactive, linked account: unusable and safe to activate later.
            await _audit.RecordAsync(SecurityAuditRecord.Success(SecurityAuditEventType.AccountInvitationAccepted, createdUserId, client), cancellationToken);
            return AccountActivationResult.Success;
        }

        private static InvitationOutcome? CheckSubject(ClinicalSubject? subject) =>
            subject switch
            {
                null or { IsActive: false } => InvitationOutcome.SubjectNotFound,
                { IdentityUserId: not null } => InvitationOutcome.SubjectAlreadyLinked,
                _ => null
            };

        private async Task<SecurityFailureReason?> CheckInvitationAsync(StoredAccountInvitation? invitation, CancellationToken cancellationToken)
        {
            if (invitation is null)
            {
                return SecurityFailureReason.InvalidToken;
            }

            if (invitation.Status != AccountInvitationStatus.Pending)
            {
                return SecurityFailureReason.InvitationNotPending;
            }

            if (invitation.ExpiresAtUtc <= _time.GetUtcNow())
            {
                return SecurityFailureReason.InvitationExpired;
            }

            var subject = await _clinicalLinks.FindSubjectAsync(invitation.SubjectType, invitation.SubjectId, cancellationToken);
            return CheckSubject(subject) is null ? null : SecurityFailureReason.SubjectUnavailable;
        }

        private async Task<InvitationResult> CreateAsync(
            ClinicalSubject subject,
            string email,
            string role,
            Guid actorUserId,
            ClientContext client,
            CancellationToken cancellationToken)
        {
            if (await _users.IsEmailInUseAsync(email, cancellationToken))
            {
                return InvitationResult.Failed(InvitationOutcome.EmailInUse);
            }

            var token = _secrets.Generate(TokenBytes);
            var expiresAt = _time.GetUtcNow().AddHours(_options.InvitationLifetimeHours);
            var id = await _invitations.AddAsync(
                new NewAccountInvitation(subject.Type, subject.Id, email, role, _secrets.Hash(token), expiresAt, actorUserId), cancellationToken);

            var link = UrlTokens.AppendQuery(_options.ActivationUrl, ("token", token));
            var emailSent = await TrySendAsync(AccountEmails.Invitation(email, link, expiresAt), cancellationToken);
            await _audit.RecordAsync(emailSent
                ? SecurityAuditRecord.Success(SecurityAuditEventType.AccountInvitationCreated, null, client) with { ActorUserId = actorUserId }
                : SecurityAuditRecord.Failure(SecurityAuditEventType.AccountInvitationCreated, null, SecurityFailureReason.EmailDeliveryFailed, client) with
                {
                    ActorUserId = actorUserId
                }, cancellationToken);

            return new InvitationResult(InvitationOutcome.Created,
                new InvitationSummary(id, subject.Type, subject.Id, email, role, expiresAt, emailSent));
        }

        // The raw token exists only in the email. A failed delivery is reported; the admin reissues the invitation.
        private async Task<bool> TrySendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            try
            {
                await _email.SendAsync(message, cancellationToken);
                return true;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return false;
            }
        }

        private async Task<AccountActivationResult> RejectAsync(SecurityFailureReason reason, ClientContext client, CancellationToken cancellationToken)
        {
            await RecordActivationFailureAsync(reason, client, cancellationToken);
            return AccountActivationResult.InvalidInvitation;
        }

        private Task RecordActivationFailureAsync(SecurityFailureReason reason, ClientContext client, CancellationToken cancellationToken) =>
            _audit.RecordAsync(SecurityAuditRecord.Failure(SecurityAuditEventType.AccountActivationFailed, null, reason, client), cancellationToken);
    }
}
