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
    // Patient registration (Patients.Create) and lookup (Patients.Read). Patients are not owned by a clinic,
    // so these use cases need the permission but not a clinic affiliation.
    public sealed class PatientService
    {
        public const int MinimumNameSearchLength = 3;
        public const int MaxSearchResults = 20;

        private readonly IClinicalReadStore _reads;
        private readonly IClinicalWriteStore _writes;
        private readonly ICurrentUser _currentUser;
        private readonly IClinicalStaffDirectory _staff;
        private readonly IPatientAccessAuthorizer _patientAccess;
        private readonly TimeProvider _time;

        public PatientService(
            IClinicalReadStore reads,
            IClinicalWriteStore writes,
            ICurrentUser currentUser,
            IClinicalStaffDirectory staff,
            IPatientAccessAuthorizer patientAccess,
            TimeProvider time)
        {
            _reads = reads;
            _writes = writes;
            _currentUser = currentUser;
            _staff = staff;
            _patientAccess = patientAccess;
            _time = time;
        }

        // Patient.Register plus, optionally, its first identifier (primary, source Manual) in the same transaction.
        public async Task<PatientSummary> RegisterAsync(RegisterPatientRequest request, CancellationToken cancellationToken)
        {
            var now = _time.GetUtcNow();
            if (request.DateOfBirth > DateOnly.FromDateTime(now.UtcDateTime))
            {
                throw new RequestValidationException("dateOfBirth cannot be in the future.", "dateOfBirth");
            }

            var patient = ClinicalInput.Apply(() => Patient.Register(
                request.GivenNames, request.FamilyNames, request.DateOfBirth, ClinicalInput.Defined(request.Sex, "sex")));

            if (request.Identifier is { } input)
            {
                RejectSystemIssued(input.Type, "identifier.type");
                // The Domain normalizes the value; the client's formatting is never the source of truth.
                var identifier = ClinicalInput.Apply(() => patient.AddIdentifier(
                    ClinicalInput.Defined(input.Type, "identifier.type"), input.Value, PatientIdentifierSource.Manual, input.Issuer, isPrimary: true));
                if (identifier.HasUniqueValue
                    && await _reads.ActiveIdentifierExistsAsync(identifier.Type, identifier.Issuer, identifier.NormalizedValue, cancellationToken))
                {
                    throw new ResourceConflictException("An active identifier with this value already belongs to another patient.");
                }
            }

            _writes.Add(patient);
            _writes.Add(ClinicalAuditEntry.Success(
                await AuditActorAsync(cancellationToken), AuditActionType.Create, nameof(Patient), patient.Id.ToString(), now,
                new AuditContext(PatientId: patient.Id)));
            await _writes.SaveChangesAsync(cancellationToken);

            return patient.ToRow().ToSummary();
        }

        public async Task<PatientSummary> GetAsync(Guid patientId, CancellationToken cancellationToken)
        {
            var patient = await _reads.GetPatientAsync(patientId, cancellationToken)
                ?? throw new ResourceNotFoundException(nameof(Patient), patientId);
            return await _patientAccess.CanReadAsync(patientId, Permissions.Patients.Read, cancellationToken)
                ? patient.ToSummary()
                : throw new AccessDeniedException("The current user cannot read this patient.");
        }

        public async Task<IReadOnlyList<PatientSummary>> SearchAsync(PatientSearchRequest request, CancellationToken cancellationToken)
        {
            var value = request.Value?.Trim() ?? string.Empty;
            IReadOnlyList<PatientRow> rows;
            if (request.IdentifierType is { } type)
            {
                ClinicalInput.Defined(type, "identifierType");
                RejectSystemIssued(type, "identifierType");
                // Same normalization as stored identifiers; an empty or malformed value is a DomainException (400).
                var normalized = PatientIdentifier.Normalize(type, value);
                rows = await _reads.FindPatientsByIdentifierAsync(type, normalized, MaxSearchResults, cancellationToken);
            }
            else
            {
                if (value.Length < MinimumNameSearchLength)
                {
                    throw new RequestValidationException(
                        $"A name search needs at least {MinimumNameSearchLength} characters.", "value");
                }

                rows = await _reads.FindPatientsByNameAsync(value, MaxSearchResults, cancellationToken);
            }

            return rows.Select(row => row.ToSummary()).ToList();
        }

        // QR credentials are only issued by the system (PatientQrService, ADR-015). A value chosen by staff could make a
        // known token resolve to the wrong patient, and searching by one would expose a lookup over credential hashes.
        private static void RejectSystemIssued(PatientIdentifierType type, string field)
        {
            if (type == PatientIdentifierType.QrCode)
            {
                throw new RequestValidationException($"{field} does not accept a system-issued identifier type.", field);
            }
        }

        private async Task<AuditActor> AuditActorAsync(CancellationToken cancellationToken)
        {
            var userId = _currentUser.UserId ?? throw new AccessDeniedException("An authenticated user is required.");
            var staff = await _staff.FindByIdentityUserAsync(userId, cancellationToken);
            return AuditActor.User(userId, staff?.StaffId);
        }
    }
}
