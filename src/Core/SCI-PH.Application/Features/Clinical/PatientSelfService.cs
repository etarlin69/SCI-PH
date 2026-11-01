using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Enums.Patients;

namespace SCI_PH.Application.Features.Clinical
{
    // Patient self-service (ADR-015): the authenticated Patient account reads its OWN record, never another one.
    // The patient is always resolved on the server: current user -> Patient.IdentityUserId -> Patient. No caller-supplied
    // patient id exists, and an encounter id is accepted only if it belongs to that patient (otherwise 404, so the
    // existence of someone else's encounter is not disclosed). It is a separate path from the staff services: there is
    // no ClinicalStaff actor, clinic or assignment here, and staff accounts are refused. Read-only.
    public sealed class PatientSelfService
    {
        // Upper bound of every list; the app shows the most recent first.
        public const int MaxItems = 200;

        private readonly ICurrentUser _currentUser;
        private readonly IClinicalIdentityLinkService _clinicalLinks;
        private readonly IPatientAccessAuthorizer _patientAccess;
        private readonly IPatientSelfReadStore _reads;

        public PatientSelfService(
            ICurrentUser currentUser,
            IClinicalIdentityLinkService clinicalLinks,
            IPatientAccessAuthorizer patientAccess,
            IPatientSelfReadStore reads)
        {
            _currentUser = currentUser;
            _clinicalLinks = clinicalLinks;
            _patientAccess = patientAccess;
            _reads = reads;
        }

        public async Task<PatientSelfProfile> GetProfileAsync(CancellationToken cancellationToken)
        {
            var patientId = await ResolveOwnPatientAsync(Permissions.Patients.Read, cancellationToken);
            var profile = await _reads.GetProfileAsync(patientId, cancellationToken) ?? throw NotLinked();
            return new PatientSelfProfile(
                profile.GivenNames,
                profile.FamilyNames,
                profile.DateOfBirth,
                profile.Sex,
                profile.IdentityStatus,
                profile.Document is { } document
                    ? new PatientIdentifierSummary(document.Type, ClinicalMapping.Mask(document.Type, document.NormalizedValue))
                    : null,
                profile.Email,
                profile.Phone);
        }

        public async Task<PatientSelfSummary> GetSummaryAsync(CancellationToken cancellationToken)
        {
            var patientId = await ResolveOwnPatientAsync(Permissions.Encounters.Read, cancellationToken);
            var profile = await _reads.GetProfileAsync(patientId, cancellationToken) ?? throw NotLinked();
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
        }

        public async Task<IReadOnlyList<PatientSelfEncounter>> ListEncountersAsync(CancellationToken cancellationToken)
        {
            var patientId = await ResolveOwnPatientAsync(Permissions.Encounters.Read, cancellationToken);
            return await _reads.ListEncountersAsync(patientId, MaxItems, cancellationToken);
        }

        public async Task<PatientSelfEncounterDetail> GetEncounterAsync(Guid encounterId, CancellationToken cancellationToken)
        {
            var patientId = await ResolveOwnPatientAsync(Permissions.Encounters.Read, cancellationToken);
            var encounter = await _reads.GetEncounterAsync(patientId, encounterId, cancellationToken)
                ?? throw new ResourceNotFoundException(nameof(ClinicalEncounter), encounterId);

            // The prescriptions of the encounter follow their own permission, as in the list.
            var prescriptions = await _patientAccess.CanReadAsync(patientId, Permissions.Medications.Read, cancellationToken)
                ? await _reads.ListPrescriptionsAsync(patientId, encounter.Id, MaxItems, cancellationToken)
                : Array.Empty<PatientSelfPrescription>();
            return new PatientSelfEncounterDetail(
                encounter,
                await _reads.ListDiagnosesAsync(patientId, encounter.Id, MaxItems, cancellationToken),
                await _reads.ListResultsAsync(patientId, encounter.Id, MaxItems, cancellationToken),
                prescriptions);
        }

        public async Task<IReadOnlyList<PatientSelfAllergy>> ListAllergiesAsync(CancellationToken cancellationToken)
        {
            var patientId = await ResolveOwnPatientAsync(Permissions.Patients.Read, cancellationToken);
            return await _reads.ListAllergiesAsync(patientId, cancellationToken);
        }

        public async Task<IReadOnlyList<PatientSelfDiagnosis>> ListDiagnosesAsync(CancellationToken cancellationToken)
        {
            var patientId = await ResolveOwnPatientAsync(Permissions.Encounters.Read, cancellationToken);
            return await _reads.ListDiagnosesAsync(patientId, null, MaxItems, cancellationToken);
        }

        public async Task<IReadOnlyList<PatientSelfPrescription>> ListPrescriptionsAsync(CancellationToken cancellationToken)
        {
            var patientId = await ResolveOwnPatientAsync(Permissions.Medications.Read, cancellationToken);
            return await _reads.ListPrescriptionsAsync(patientId, null, MaxItems, cancellationToken);
        }

        public async Task<IReadOnlyList<PatientSelfResult>> ListResultsAsync(CancellationToken cancellationToken)
        {
            var patientId = await ResolveOwnPatientAsync(Permissions.Encounters.Read, cancellationToken);
            return await _reads.ListResultsAsync(patientId, null, MaxItems, cancellationToken);
        }

        // Fails closed: a Patient account without its clinical link is refused (403) and is never linked here by email,
        // name, document or phone. The authorizer then confirms the own-record rule for the permission of the area.
        private async Task<Guid> ResolveOwnPatientAsync(string permission, CancellationToken cancellationToken)
        {
            if (!_currentUser.IsAuthenticated
                || _currentUser.UserId is not { } userId
                || !_currentUser.Roles.Contains(SecurityRoles.Patient))
            {
                throw new AccessDeniedException("Patient self-service requires a patient account.");
            }

            var patientId = await _clinicalLinks.FindPatientIdByUserAsync(userId, cancellationToken) ?? throw NotLinked();
            return await _patientAccess.CanReadAsync(patientId, permission, cancellationToken)
                ? patientId
                : throw new AccessDeniedException("The patient account cannot read this part of its record.");
        }

        private static AccessDeniedException NotLinked() =>
            new("The patient account is not linked to a clinical record.");
    }
}
