using Microsoft.Extensions.Logging;
using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Domain.Entities.Auditing;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Patients;
using SCI_PH.Domain.ValueObjects;

namespace SCI_PH.Application.Features.Clinical
{
    // The patient's own QR credential (ADR-015): it identifies the clinical patient to authorized staff and never
    // authenticates anyone. The patient comes from the session (Patient.IdentityUserId), never from the request.
    //
    // One active credential per patient. Issuing revokes the current one and creates another in ONE transaction, with their
    // audit entries; without them nothing changes (generic 503). PatientIdentifier's unique index of active primaries
    // (UX_PatientIdentifiers_ActivePrimary) is the final guard: of two concurrent issues one commits and the other rolls
    // back with a 409, so a token is returned only if it was committed as the single active one.
    //
    // A new credential needs an account active NOW: the access token alone is not enough, as one issued before the account
    // was disabled stays valid for minutes (ADR-006). Credentials already issued do not depend on the account; they end when
    // they expire or are revoked. Revoking never needs the check: a credential can always be withdrawn.
    public sealed class PatientQrService
    {
        public const string AuditResource = "PatientQrCode";

        private const string Unavailable = "Your QR code is not available right now. Try again later.";
        private const string NotAPatientFailure = "NotAPatientAccount";
        private const string InactiveAccountFailure = "AccountInactive";
        private const string NotLinkedFailure = "PatientNotLinked";

        private readonly ICurrentUser _currentUser;
        private readonly IIdentityService _identity;
        private readonly IClinicalIdentityLinkService _clinicalLinks;
        private readonly IPatientQrCredentialStore _credentials;
        private readonly IClinicalWriteStore _writes;
        private readonly TimeProvider _time;
        private readonly ILogger<PatientQrService> _logger;

        public PatientQrService(
            ICurrentUser currentUser,
            IIdentityService identity,
            IClinicalIdentityLinkService clinicalLinks,
            IPatientQrCredentialStore credentials,
            IClinicalWriteStore writes,
            TimeProvider time,
            ILogger<PatientQrService> logger)
        {
            _currentUser = currentUser;
            _identity = identity;
            _clinicalLinks = clinicalLinks;
            _credentials = credentials;
            _writes = writes;
            _time = time;
            _logger = logger;
        }

        public async Task<PatientQrCredential> IssueAsync(CancellationToken cancellationToken)
        {
            var userId = RequirePatientAccount();
            await RequireActiveAccountAsync(userId, cancellationToken);
            var patientId = await OwnPatientAsync(userId, AuditActionType.Create, cancellationToken);
            var patient = await _credentials.LoadPatientWithActiveQrAsync(patientId, cancellationToken) ?? throw NotLinked();

            // The expiry and every later check use the server's clock (TimeProvider).
            var now = _time.GetUtcNow();
            RevokeActive(patient, userId, now);

            var (payload, hash) = PatientQrToken.Generate();
            var expiresAt = now + PatientQrToken.Lifetime;
            var credential = patient.AddIdentifier(
                PatientIdentifierType.QrCode, hash, PatientIdentifierSource.System, isPrimary: true, validUntilUtc: expiresAt);
            _credentials.Add(credential);
            _writes.Add(ClinicalAuditEntry.Success(
                AuditActor.User(userId), AuditActionType.Create, AuditResource, credential.Id.ToString(), now, new AuditContext(patient.Id)));

            await SaveAsync(cancellationToken);
            return new PatientQrCredential(payload, now, expiresAt);
        }

        // Idempotent: without an active credential nothing changes and nothing is recorded.
        public async Task RevokeAsync(CancellationToken cancellationToken)
        {
            var userId = RequirePatientAccount();
            var patientId = await OwnPatientAsync(userId, AuditActionType.Delete, cancellationToken);
            var patient = await _credentials.LoadPatientWithActiveQrAsync(patientId, cancellationToken) ?? throw NotLinked();
            if (RevokeActive(patient, userId, _time.GetUtcNow()) > 0)
            {
                await SaveAsync(cancellationToken);
            }
        }

        private int RevokeActive(Patient patient, Guid userId, DateTimeOffset now)
        {
            var active = patient.Identifiers.Where(i => i.Type == PatientIdentifierType.QrCode && i.IsActive).ToList();
            foreach (var credential in active)
            {
                // The Domain stamps CreatedAtUtc from its own clock and rejects a revocation before it: another server's
                // clock may be slightly behind, and that must not block taking a credential out of service.
                patient.RevokeIdentifier(credential.Id, now > credential.CreatedAtUtc ? now : credential.CreatedAtUtc);
                _writes.Add(ClinicalAuditEntry.Success(
                    AuditActor.User(userId), AuditActionType.Delete, AuditResource, credential.Id.ToString(), now, new AuditContext(patient.Id)));
            }

            return active.Count;
        }

        private Guid RequirePatientAccount()
        {
            if (!_currentUser.IsAuthenticated || _currentUser.UserId is not { } userId || !_currentUser.Roles.Contains(SecurityRoles.Patient))
            {
                throw new AccessDeniedException("The QR code requires a patient account.");
            }

            return userId;
        }

        private async Task RequireActiveAccountAsync(Guid userId, CancellationToken cancellationToken)
        {
            if (await _identity.FindActiveAccountAsync(userId, cancellationToken) is null)
            {
                await AuditDeniedAsync(userId, AuditActionType.Create, InactiveAccountFailure, cancellationToken);
                throw new AccessDeniedException("The QR code requires a patient account.");
            }
        }

        // Fails closed: an account without its clinical link is refused and never linked here.
        private async Task<Guid> OwnPatientAsync(Guid userId, AuditActionType action, CancellationToken cancellationToken)
        {
            if (await _clinicalLinks.FindPatientIdByUserAsync(userId, cancellationToken) is { } patientId)
            {
                return patientId;
            }

            await AuditDeniedAsync(userId, action, NotLinkedFailure, cancellationToken);
            throw NotLinked();
        }

        // One transaction: without the audit entries, no credential changes and no token leaves. A 409 of a concurrent issue
        // keeps its meaning. Only the exception type is logged.
        private async Task SaveAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _writes.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException and not ResourceConflictException)
            {
                _logger.LogError("Patient QR credential not changed: saving it failed with {ExceptionType}.", exception.GetType().Name);
                throw new ExternalServiceUnavailableException(Unavailable);
            }
        }

        // Best effort: the request is denied whether or not this is recorded.
        private async Task AuditDeniedAsync(Guid userId, AuditActionType action, string failure, CancellationToken cancellationToken)
        {
            try
            {
                _writes.Add(ClinicalAuditEntry.Failure(
                    AuditActor.User(userId), action, AuditResource, userId.ToString(), _time.GetUtcNow(), failure, new AuditContext()));
                await _writes.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning("Denied patient QR operation not audited: {ExceptionType}.", exception.GetType().Name);
            }
        }

        private static AccessDeniedException NotLinked() =>
            new("The patient account is not linked to a clinical record.");
    }
}
