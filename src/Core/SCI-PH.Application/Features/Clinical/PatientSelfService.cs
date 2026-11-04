using Microsoft.Extensions.Logging;
using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Domain.Entities.Auditing;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Patients;
using SCI_PH.Domain.ValueObjects;

namespace SCI_PH.Application.Features.Clinical
{
    // Resource category of each patient self-service operation, as recorded in ClinicalAuditEntry.EntityName.
    public static class PatientSelfAuditResources
    {
        public const string Profile = "PatientProfile";
        public const string Summary = "PatientSummary";
        public const string Encounters = "PatientEncounters";
        public const string Encounter = "PatientEncounter";
        public const string Allergies = "PatientAllergies";
        public const string Diagnoses = "PatientDiagnoses";
        public const string Prescriptions = "PatientPrescriptions";
        public const string Results = "PatientResults";
    }

    // Patient self-service (ADR-015): the authenticated Patient account reads its OWN record, never another one.
    // The patient is always resolved on the server: current user -> Patient.IdentityUserId -> Patient. No caller-supplied
    // patient id exists, and an encounter id is accepted only if it belongs to that patient (otherwise 404, so the
    // existence of someone else's encounter is not disclosed). It is a separate path from the staff services: there is
    // no ClinicalStaff actor, clinic or assignment here, and staff accounts are refused. Read-only.
    //
    // Every read is audited, one entry per operation, with metadata only (who, resource category, outcome, time): never
    // record content. It fails closed: this is not an emergency care path, so without its audit entry the payload is not
    // returned (generic 503). A denied read is audited on a best-effort basis: failing to record it never changes the denial.
    public sealed class PatientSelfService
    {
        // Upper bound of every list; the app shows the most recent first.
        public const int MaxItems = 200;

        private const string Unavailable = "Your record is not available right now. Try again later.";
        private const string NotLinkedFailure = "PatientNotLinked";
        private const string NotPatientFailure = "NotAPatientAccount";
        private const string AccessDeniedFailure = "AccessDenied";
        private const string NotFoundFailure = "NotFound";

        private readonly ICurrentUser _currentUser;
        private readonly IClinicalIdentityLinkService _clinicalLinks;
        private readonly IPatientAccessAuthorizer _patientAccess;
        private readonly IPatientSelfReadStore _reads;
        private readonly IClinicalWriteStore _writes;
        private readonly TimeProvider _time;
        private readonly ILogger<PatientSelfService> _logger;

        public PatientSelfService(
            ICurrentUser currentUser,
            IClinicalIdentityLinkService clinicalLinks,
            IPatientAccessAuthorizer patientAccess,
            IPatientSelfReadStore reads,
            IClinicalWriteStore writes,
            TimeProvider time,
            ILogger<PatientSelfService> logger)
        {
            _currentUser = currentUser;
            _clinicalLinks = clinicalLinks;
            _patientAccess = patientAccess;
            _reads = reads;
            _writes = writes;
            _time = time;
            _logger = logger;
        }

        public Task<PatientSelfProfile> GetProfileAsync(CancellationToken cancellationToken) =>
            ReadAsync(PatientSelfAuditResources.Profile, Permissions.Patients.Read, async patientId =>
            {
                var profile = await FindProfileAsync(patientId, PatientSelfAuditResources.Profile, cancellationToken);
                return new PatientSelfProfile(
                    profile.GivenNames,
                    profile.FamilyNames,
                    profile.DateOfBirth,
                    profile.Sex,
                    profile.Document is { } document
                        ? new PatientIdentifierSummary(document.Type, ClinicalMapping.Mask(document.Type, document.NormalizedValue))
                        : null,
                    profile.Email,
                    profile.Phone);
            }, cancellationToken);

        public Task<PatientSelfSummary> GetSummaryAsync(CancellationToken cancellationToken) =>
            ReadAsync(PatientSelfAuditResources.Summary, Permissions.Encounters.Read, async patientId =>
            {
                var profile = await FindProfileAsync(patientId, PatientSelfAuditResources.Summary, cancellationToken);
                var latestEncounter = await _reads.ListEncountersAsync(patientId, 1, cancellationToken);
                var latestResult = await _reads.ListResultsAsync(patientId, null, 1, cancellationToken);
                var allergies = await _reads.ListAllergiesAsync(patientId, cancellationToken);
                return new PatientSelfSummary(
                    $"{profile.GivenNames} {profile.FamilyNames}",
                    await _reads.CountEncountersAsync(patientId, cancellationToken),
                    await _reads.CountResultsAsync(patientId, cancellationToken),
                    latestEncounter.FirstOrDefault(),
                    latestResult.FirstOrDefault(),
                    allergies.Where(a => a.Status == ClinicalRecordStatus.Active).ToList());
            }, cancellationToken);

        public Task<IReadOnlyList<PatientSelfEncounter>> ListEncountersAsync(CancellationToken cancellationToken) =>
            ReadAsync(PatientSelfAuditResources.Encounters, Permissions.Encounters.Read,
                patientId => _reads.ListEncountersAsync(patientId, MaxItems, cancellationToken), cancellationToken);

        public async Task<PatientSelfEncounterDetail> GetEncounterAsync(Guid encounterId, CancellationToken cancellationToken)
        {
            var (userId, patientId) = await ResolveOwnPatientAsync(
                Permissions.Encounters.Read, PatientSelfAuditResources.Encounter, cancellationToken);
            var encounter = await _reads.GetEncounterAsync(patientId, encounterId, cancellationToken);
            if (encounter is null)
            {
                // Someone else's encounter and a missing one are the same 404. The attempted id is a route Guid, not content.
                await AuditDeniedAsync(PatientSelfAuditResources.Encounter, userId, encounterId.ToString(), patientId, NotFoundFailure, cancellationToken);
                throw new ResourceNotFoundException(nameof(ClinicalEncounter), encounterId);
            }

            // The prescriptions of the encounter follow their own permission, as in the list.
            var prescriptions = await _patientAccess.CanReadAsync(patientId, Permissions.Medications.Read, cancellationToken)
                ? await _reads.ListPrescriptionsAsync(patientId, encounter.Id, MaxItems, cancellationToken)
                : Array.Empty<PatientSelfPrescription>();
            var detail = new PatientSelfEncounterDetail(
                encounter,
                await _reads.ListDiagnosesAsync(patientId, encounter.Id, MaxItems, cancellationToken),
                await _reads.ListResultsAsync(patientId, encounter.Id, MaxItems, cancellationToken),
                prescriptions);
            await AuditReadAsync(PatientSelfAuditResources.Encounter, userId, encounter.Id.ToString(), patientId, encounter.Id, cancellationToken);
            return detail;
        }

        public Task<IReadOnlyList<PatientSelfAllergy>> ListAllergiesAsync(CancellationToken cancellationToken) =>
            ReadAsync(PatientSelfAuditResources.Allergies, Permissions.Patients.Read,
                patientId => _reads.ListAllergiesAsync(patientId, cancellationToken), cancellationToken);

        public Task<IReadOnlyList<PatientSelfDiagnosis>> ListDiagnosesAsync(CancellationToken cancellationToken) =>
            ReadAsync(PatientSelfAuditResources.Diagnoses, Permissions.Encounters.Read,
                patientId => _reads.ListDiagnosesAsync(patientId, null, MaxItems, cancellationToken), cancellationToken);

        public Task<IReadOnlyList<PatientSelfPrescription>> ListPrescriptionsAsync(CancellationToken cancellationToken) =>
            ReadAsync(PatientSelfAuditResources.Prescriptions, Permissions.Medications.Read,
                patientId => _reads.ListPrescriptionsAsync(patientId, null, MaxItems, cancellationToken), cancellationToken);

        public Task<IReadOnlyList<PatientSelfResult>> ListResultsAsync(CancellationToken cancellationToken) =>
            ReadAsync(PatientSelfAuditResources.Results, Permissions.Encounters.Read,
                patientId => _reads.ListResultsAsync(patientId, null, MaxItems, cancellationToken), cancellationToken);

        // Resolve the own record -> read -> audit -> only then the payload leaves this service.
        private async Task<T> ReadAsync<T>(string resource, string permission, Func<Guid, Task<T>> read, CancellationToken cancellationToken)
        {
            var (userId, patientId) = await ResolveOwnPatientAsync(permission, resource, cancellationToken);
            var payload = await read(patientId);
            await AuditReadAsync(resource, userId, patientId.ToString(), patientId, null, cancellationToken);
            return payload;
        }

        private async Task<PatientSelfProfileRow> FindProfileAsync(Guid patientId, string resource, CancellationToken cancellationToken)
        {
            var profile = await _reads.GetProfileAsync(patientId, cancellationToken);
            if (profile is not null)
            {
                return profile;
            }

            await AuditDeniedAsync(resource, _currentUser.UserId!.Value, patientId.ToString(), patientId, NotLinkedFailure, cancellationToken);
            throw NotLinked();
        }

        // Fails closed: a Patient account without its clinical link is refused (403) and is never linked here by email,
        // name, document or phone. The authorizer then confirms the own-record rule for the permission of the area.
        private async Task<(Guid UserId, Guid PatientId)> ResolveOwnPatientAsync(string permission, string resource, CancellationToken cancellationToken)
        {
            // Without an authenticated user there is no actor to audit.
            if (!_currentUser.IsAuthenticated || _currentUser.UserId is not { } userId)
            {
                throw new AccessDeniedException("Patient self-service requires a patient account.");
            }

            if (!_currentUser.Roles.Contains(SecurityRoles.Patient))
            {
                await AuditDeniedAsync(resource, userId, userId.ToString(), null, NotPatientFailure, cancellationToken);
                throw new AccessDeniedException("Patient self-service requires a patient account.");
            }

            if (await _clinicalLinks.FindPatientIdByUserAsync(userId, cancellationToken) is not { } patientId)
            {
                await AuditDeniedAsync(resource, userId, userId.ToString(), null, NotLinkedFailure, cancellationToken);
                throw NotLinked();
            }

            if (!await _patientAccess.CanReadAsync(patientId, permission, cancellationToken))
            {
                await AuditDeniedAsync(resource, userId, patientId.ToString(), patientId, AccessDeniedFailure, cancellationToken);
                throw new AccessDeniedException("The patient account cannot read this part of its record.");
            }

            return (userId, patientId);
        }

        // Required: without this entry the caller gets the generic 503 instead of the payload. Only the exception type is
        // logged, and nothing about the failure reaches the client.
        private async Task AuditReadAsync(
            string resource, Guid userId, string entityId, Guid patientId, Guid? encounterId, CancellationToken cancellationToken)
        {
            try
            {
                _writes.Add(ClinicalAuditEntry.Success(
                    AuditActor.User(userId), AuditActionType.Read, resource, entityId, _time.GetUtcNow(), new AuditContext(patientId, encounterId)));
                await _writes.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError("Patient self-service read of {Resource} not returned: its audit failed with {ExceptionType}.", resource, exception.GetType().Name);
                throw new ExternalServiceUnavailableException(Unavailable);
            }
        }

        // Best effort: the request is denied whether or not this is recorded, so a failure here must not replace the denial.
        private async Task AuditDeniedAsync(
            string resource, Guid userId, string entityId, Guid? patientId, string failure, CancellationToken cancellationToken)
        {
            try
            {
                _writes.Add(ClinicalAuditEntry.Failure(
                    AuditActor.User(userId), AuditActionType.Read, resource, entityId, _time.GetUtcNow(), failure, new AuditContext(patientId)));
                await _writes.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning("Denied patient self-service read of {Resource} not audited: {ExceptionType}.", resource, exception.GetType().Name);
            }
        }

        private static AccessDeniedException NotLinked() =>
            new("The patient account is not linked to a clinical record.");
    }
}
