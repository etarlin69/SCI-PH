using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Domain.Entities.Auditing;
using SCI_PH.Domain.Entities.Organizations;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Organizations;
using SCI_PH.Domain.Exceptions;
using SCI_PH.Domain.ValueObjects;

namespace SCI_PH.Application.Features.Clinical
{
    // Administration of the clinical organization: clinics (Clinics.Manage), clinical staff and their clinic affiliations
    // (ClinicalStaff.Manage). Only what the Domain supports: register, activate, deactivate, start and end affiliations.
    // There is no update and no delete; history is kept. Accounts stay in Identity: a staff member gets one through the
    // existing staff invitation (Users.Manage), which links ClinicalStaff.IdentityUserId on activation.
    // ICurrentClinicalActor already ignores inactive staff, inactive clinics and ended affiliations, so deactivating
    // never ends affiliations automatically.
    public sealed class ClinicalOrganizationService
    {
        private readonly IClinicalOrganizationStore _store;
        private readonly ICurrentUser _currentUser;
        private readonly TimeProvider _time;

        public ClinicalOrganizationService(IClinicalOrganizationStore store, ICurrentUser currentUser, TimeProvider time)
        {
            _store = store;
            _currentUser = currentUser;
            _time = time;
        }

        private AuditActor Actor => AuditActor.User(
            _currentUser.UserId ?? throw new AccessDeniedException("An authenticated user is required."));

        // ---- Clinics --------------------------------------------------------------------------------------------------

        public Task<IReadOnlyList<ClinicAdminSummary>> ListClinicsAsync(string? search, ClinicStatus? status, CancellationToken cancellationToken) =>
            _store.ListClinicsAsync(Search(search), status, cancellationToken);

        public async Task<ClinicAdminSummary> GetClinicAsync(Guid clinicId, CancellationToken cancellationToken) =>
            await _store.GetClinicAsync(clinicId, cancellationToken) ?? throw new ResourceNotFoundException(nameof(Clinic), clinicId);

        public async Task<ClinicAdminSummary> RegisterClinicAsync(RegisterClinicRequest request, CancellationToken cancellationToken)
        {
            ClinicalInput.Defined(request.Type, "type");
            var clinic = ClinicalInput.Apply(() => Clinic.Register(
                request.Name, request.Type, request.Province, request.Municipality, request.FacilityCode, request.Address, request.PhoneNumber));

            _store.Add(clinic);
            Audit(AuditActionType.Create, nameof(Clinic), clinic.Id);
            await _store.SaveChangesAsync(cancellationToken);
            return await GetClinicAsync(clinic.Id, cancellationToken);
        }

        // Idempotent: a clinic already in the requested status is left as is.
        public async Task SetClinicActiveAsync(Guid clinicId, bool active, CancellationToken cancellationToken)
        {
            var clinic = await _store.FindClinicAsync(clinicId, cancellationToken)
                ?? throw new ResourceNotFoundException(nameof(Clinic), clinicId);
            var target = active ? ClinicStatus.Active : ClinicStatus.Inactive;
            if (clinic.Status == target)
            {
                return;
            }

            if (active)
            {
                clinic.Activate();
            }
            else
            {
                clinic.Deactivate();
            }

            Audit(AuditActionType.Update, nameof(Clinic), clinic.Id);
            await _store.SaveChangesAsync(cancellationToken);
        }

        // ---- Clinical staff -------------------------------------------------------------------------------------------

        public Task<IReadOnlyList<ClinicalStaffAdminSummary>> ListStaffAsync(string? search, StaffStatus? status, CancellationToken cancellationToken) =>
            _store.ListStaffAsync(Search(search), status, cancellationToken);

        public async Task<ClinicalStaffAdminSummary> GetStaffAsync(Guid staffId, CancellationToken cancellationToken) =>
            await _store.GetStaffAsync(staffId, cancellationToken) ?? throw new ResourceNotFoundException(nameof(ClinicalStaff), staffId);

        public async Task<ClinicalStaffAdminSummary> RegisterStaffAsync(RegisterClinicalStaffRequest request, CancellationToken cancellationToken)
        {
            ClinicalInput.Defined(request.Type, "type");
            var staff = ClinicalInput.Apply(() => ClinicalStaff.Register(
                request.GivenNames, request.FamilyNames, request.Type, request.ProfessionalLicenseNumber, request.Specialty, request.PhoneNumber));

            _store.Add(staff);
            Audit(AuditActionType.Create, nameof(ClinicalStaff), staff.Id);
            await _store.SaveChangesAsync(cancellationToken);
            return await GetStaffAsync(staff.Id, cancellationToken);
        }

        // Idempotent. The linked account (if any) is not touched: Identity and clinical staff have separate lifecycles.
        public async Task SetStaffActiveAsync(Guid staffId, bool active, CancellationToken cancellationToken)
        {
            var staff = await _store.FindStaffAsync(staffId, cancellationToken)
                ?? throw new ResourceNotFoundException(nameof(ClinicalStaff), staffId);
            var target = active ? StaffStatus.Active : StaffStatus.Inactive;
            if (staff.Status == target)
            {
                return;
            }

            if (active)
            {
                staff.Activate();
            }
            else
            {
                staff.Deactivate();
            }

            Audit(AuditActionType.Update, nameof(ClinicalStaff), staff.Id);
            await _store.SaveChangesAsync(cancellationToken);
        }

        // ---- Affiliations ---------------------------------------------------------------------------------------------

        public async Task<IReadOnlyList<ClinicStaffAffiliationSummary>> ListAffiliationsAsync(Guid staffId, CancellationToken cancellationToken)
        {
            await GetStaffAsync(staffId, cancellationToken);
            return await _store.ListAffiliationsAsync(staffId, cancellationToken);
        }

        // ClinicStaffAffiliation.Start now. Staff and clinic must exist (404) and be active (409); one open affiliation per
        // staff member and clinic (409, also under concurrent requests through UX_ClinicStaffAffiliations_Active).
        public async Task<ClinicStaffAffiliationSummary> StartAffiliationAsync(
            Guid staffId, StartAffiliationRequest request, CancellationToken cancellationToken)
        {
            ClinicalInput.Id(request.ClinicId, "clinicId");
            var staff = await _store.FindStaffAsync(staffId, cancellationToken)
                ?? throw new ResourceNotFoundException(nameof(ClinicalStaff), staffId);
            var clinic = await _store.FindClinicAsync(request.ClinicId, cancellationToken)
                ?? throw new ResourceNotFoundException(nameof(Clinic), request.ClinicId);
            if (staff.Status != StaffStatus.Active)
            {
                throw new ResourceConflictException("An inactive staff member cannot be affiliated with a clinic.");
            }

            if (clinic.Status != ClinicStatus.Active)
            {
                throw new ResourceConflictException("A staff member cannot be affiliated with an inactive clinic.");
            }

            if (await _store.HasActiveAffiliationAsync(staff.Id, clinic.Id, cancellationToken))
            {
                throw new ResourceConflictException("The staff member already has an open affiliation with this clinic.");
            }

            var affiliation = ClinicalInput.Apply(() => ClinicStaffAffiliation.Start(clinic.Id, staff.Id, _time.GetUtcNow()));
            _store.Add(affiliation);
            Audit(AuditActionType.Create, nameof(ClinicStaffAffiliation), affiliation.Id);
            await _store.SaveChangesAsync(cancellationToken);

            return (await _store.ListAffiliationsAsync(staffId, cancellationToken)).Single(a => a.Id == affiliation.Id);
        }

        // ClinicStaffAffiliation.End now; the row is kept as history. Ending it twice is a conflict.
        public async Task<ClinicStaffAffiliationSummary> EndAffiliationAsync(Guid staffId, Guid affiliationId, CancellationToken cancellationToken)
        {
            var affiliation = await _store.FindAffiliationAsync(staffId, affiliationId, cancellationToken)
                ?? throw new ResourceNotFoundException(nameof(ClinicStaffAffiliation), affiliationId);
            try
            {
                ClinicalInput.Apply(() => affiliation.End(_time.GetUtcNow()));
            }
            catch (DomainException exception)
            {
                throw new ResourceConflictException(exception.Message, exception);
            }

            Audit(AuditActionType.Update, nameof(ClinicStaffAffiliation), affiliation.Id);
            await _store.SaveChangesAsync(cancellationToken);

            return (await _store.ListAffiliationsAsync(staffId, cancellationToken)).Single(a => a.Id == affiliation.Id);
        }

        private void Audit(AuditActionType action, string entity, Guid id) =>
            _store.Add(ClinicalAuditEntry.Success(Actor, action, entity, id.ToString(), _time.GetUtcNow()));

        private static string? Search(string? search) =>
            string.IsNullOrWhiteSpace(search) ? null : search.Trim();
    }
}
