using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.Features.Clinical;
using SCI_PH.Domain.Entities.Auditing;
using SCI_PH.Domain.Entities.Organizations;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Organizations;

namespace SCI_PH.Application.Tests.Clinical
{
    public class ClinicalOrganizationTests
    {
        private static readonly CancellationToken None = CancellationToken.None;
        private static readonly DateTimeOffset Now = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

        private static (ClinicalOrganizationService Service, FakeOrganizationStore Store, FixedTime Time) Create()
        {
            var store = new FakeOrganizationStore();
            var time = new FixedTime(Now);
            return (new ClinicalOrganizationService(store, FakeCurrentUser.InRole(SecurityRoles.Administrator), time), store, time);
        }

        private static RegisterClinicRequest Clinic(string name = "Hospital Central", string? code = null) =>
            new(name, ClinicType.Hospital, "Distrito Nacional", "Santo Domingo", code, "Av. Principal 1", "809-555-0101");

        private static RegisterClinicalStaffRequest Doctor(string? license = "MED-001") =>
            new("Laura", "Gómez", ClinicalStaffType.Physician, license, "Medicina interna");

        [Fact]
        public async Task ClinicsAreRegisteredWithTheDomainFieldsListedAndAudited()
        {
            var (service, store, _) = Create();

            var clinic = await service.RegisterClinicAsync(Clinic(code: "hc-01"), None);
            await service.RegisterClinicAsync(Clinic("Clínica Norte"), None);

            Assert.Equal("HC-01", clinic.FacilityCode);
            Assert.Equal(ClinicStatus.Active, clinic.Status);
            Assert.Equal(["Clínica Norte", "Hospital Central"], (await service.ListClinicsAsync(null, null, None)).Select(c => c.Name));
            Assert.Single(await service.ListClinicsAsync("  norte ", null, None));
            Assert.All(store.Audit, a => Assert.Equal(AuditActionType.Create, a.Action));
            Assert.Equal(2, store.Audit.Count);
        }

        [Fact]
        public async Task InvalidClinicsAreValidationErrorsWithTheirField()
        {
            var (service, store, _) = Create();

            var name = await Assert.ThrowsAsync<RequestValidationException>(() => service.RegisterClinicAsync(Clinic(" "), None));
            var type = await Assert.ThrowsAsync<RequestValidationException>(() =>
                service.RegisterClinicAsync(Clinic() with { Type = (ClinicType)99 }, None));
            var province = await Assert.ThrowsAsync<RequestValidationException>(() =>
                service.RegisterClinicAsync(Clinic() with { Province = "" }, None));

            Assert.Equal("name", name.Field);
            Assert.Equal("type", type.Field);
            Assert.Equal("province", province.Field);
            Assert.Empty(store.Clinics);
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.GetClinicAsync(Guid.NewGuid(), None));
        }

        [Fact]
        public async Task ClinicActivationIsIdempotentAndOnlyRealChangesAreAudited()
        {
            var (service, store, _) = Create();
            var clinic = await service.RegisterClinicAsync(Clinic(), None);

            await service.SetClinicActiveAsync(clinic.Id, active: false, None);
            await service.SetClinicActiveAsync(clinic.Id, active: false, None);

            Assert.Equal(ClinicStatus.Inactive, (await service.GetClinicAsync(clinic.Id, None)).Status);
            Assert.Single(await service.ListClinicsAsync(null, ClinicStatus.Inactive, None));
            Assert.Single(store.Audit, a => a.Action == AuditActionType.Update);
            await service.SetClinicActiveAsync(clinic.Id, active: true, None);
            Assert.Equal(ClinicStatus.Active, (await service.GetClinicAsync(clinic.Id, None)).Status);
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.SetClinicActiveAsync(Guid.NewGuid(), true, None));
        }

        [Fact]
        public async Task StaffIsRegisteredWithoutAnAccountAndShowsTheLinkOnceActivated()
        {
            var (service, store, _) = Create();

            var staff = await service.RegisterStaffAsync(Doctor("med-001"), None);

            Assert.False(staff.AccountLinked);
            Assert.Null(staff.IdentityUserId);
            Assert.Equal("MED-001", staff.ProfessionalLicenseNumber);
            Assert.Equal(StaffStatus.Active, staff.Status);
            Assert.Empty(staff.ActiveClinics);
            // The existing invitation activation links the account (ClinicalStaff.LinkIdentityUser).
            var userId = Guid.NewGuid();
            store.Staff.Single().LinkIdentityUser(userId);
            var linked = await service.GetStaffAsync(staff.Id, None);
            Assert.True(linked.AccountLinked);
            Assert.Equal(userId, linked.IdentityUserId);
            Assert.Single(await service.ListStaffAsync("gómez", null, None));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.GetStaffAsync(Guid.NewGuid(), None));
        }

        [Fact]
        public async Task InvalidStaffIsAValidationError()
        {
            var (service, store, _) = Create();

            var given = await Assert.ThrowsAsync<RequestValidationException>(() =>
                service.RegisterStaffAsync(Doctor() with { GivenNames = "" }, None));
            var type = await Assert.ThrowsAsync<RequestValidationException>(() =>
                service.RegisterStaffAsync(Doctor() with { Type = (ClinicalStaffType)0 }, None));

            Assert.Equal("givenNames", given.Field);
            Assert.Equal("type", type.Field);
            Assert.Empty(store.Staff);
        }

        [Fact]
        public async Task StaffActivationIsIdempotentAndKeepsTheAccountLink()
        {
            var (service, store, _) = Create();
            var staff = await service.RegisterStaffAsync(Doctor(), None);
            store.Staff.Single().LinkIdentityUser(Guid.NewGuid());

            await service.SetStaffActiveAsync(staff.Id, active: false, None);
            await service.SetStaffActiveAsync(staff.Id, active: false, None);

            var inactive = await service.GetStaffAsync(staff.Id, None);
            Assert.Equal(StaffStatus.Inactive, inactive.Status);
            Assert.True(inactive.AccountLinked);
            Assert.Single(await service.ListStaffAsync(null, StaffStatus.Inactive, None));
            Assert.Single(store.Audit, a => a.Action == AuditActionType.Update);
        }

        [Fact]
        public async Task AStaffMemberCanBeAffiliatedWithSeveralClinicsButOncePerClinic()
        {
            var (service, store, _) = Create();
            var clinicA = await service.RegisterClinicAsync(Clinic("Hospital A"), None);
            var clinicB = await service.RegisterClinicAsync(Clinic("Hospital B"), None);
            var staff = await service.RegisterStaffAsync(Doctor(), None);

            var first = await service.StartAffiliationAsync(staff.Id, new StartAffiliationRequest(clinicA.Id), None);
            await service.StartAffiliationAsync(staff.Id, new StartAffiliationRequest(clinicB.Id), None);
            await Assert.ThrowsAsync<ResourceConflictException>(() =>
                service.StartAffiliationAsync(staff.Id, new StartAffiliationRequest(clinicA.Id), None));

            Assert.True(first.IsActive);
            Assert.Equal(Now, first.StartedAtUtc);
            Assert.Equal(["Hospital A", "Hospital B"], (await service.GetStaffAsync(staff.Id, None)).ActiveClinics.Select(c => c.Name));
            Assert.Equal(1, (await service.GetClinicAsync(clinicA.Id, None)).ActiveStaffCount);
            Assert.Equal(2, store.Affiliations.Count);
        }

        [Fact]
        public async Task AffiliationsNeedAnExistingAndActiveStaffMemberAndClinic()
        {
            var (service, _, _) = Create();
            var clinic = await service.RegisterClinicAsync(Clinic(), None);
            var inactiveClinic = await service.RegisterClinicAsync(Clinic("Cerrada"), None);
            await service.SetClinicActiveAsync(inactiveClinic.Id, false, None);
            var staff = await service.RegisterStaffAsync(Doctor(), None);
            var inactiveStaff = await service.RegisterStaffAsync(Doctor("MED-002"), None);
            await service.SetStaffActiveAsync(inactiveStaff.Id, false, None);

            var missingStaff = await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
                service.StartAffiliationAsync(Guid.NewGuid(), new StartAffiliationRequest(clinic.Id), None));
            var missingClinic = await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
                service.StartAffiliationAsync(staff.Id, new StartAffiliationRequest(Guid.NewGuid()), None));
            await Assert.ThrowsAsync<ResourceConflictException>(() =>
                service.StartAffiliationAsync(staff.Id, new StartAffiliationRequest(inactiveClinic.Id), None));
            await Assert.ThrowsAsync<ResourceConflictException>(() =>
                service.StartAffiliationAsync(inactiveStaff.Id, new StartAffiliationRequest(clinic.Id), None));
            var empty = await Assert.ThrowsAsync<RequestValidationException>(() =>
                service.StartAffiliationAsync(staff.Id, new StartAffiliationRequest(Guid.Empty), None));

            Assert.Equal(nameof(ClinicalStaff), missingStaff.Resource);
            Assert.Equal(nameof(Clinic), missingClinic.Resource);
            Assert.Equal("clinicId", empty.Field);
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.ListAffiliationsAsync(Guid.NewGuid(), None));
        }

        [Fact]
        public async Task EndingAnAffiliationKeepsItAsHistoryAndAllowsAReturn()
        {
            var (service, store, time) = Create();
            var clinic = await service.RegisterClinicAsync(Clinic(), None);
            var staff = await service.RegisterStaffAsync(Doctor(), None);
            var other = await service.RegisterStaffAsync(Doctor("MED-002"), None);
            var affiliation = await service.StartAffiliationAsync(staff.Id, new StartAffiliationRequest(clinic.Id), None);
            time.Now = Now.AddDays(30);

            var ended = await service.EndAffiliationAsync(staff.Id, affiliation.Id, None);

            Assert.False(ended.IsActive);
            Assert.Equal(Now.AddDays(30), ended.EndedAtUtc);
            await Assert.ThrowsAsync<ResourceConflictException>(() => service.EndAffiliationAsync(staff.Id, affiliation.Id, None));
            // Another staff member's affiliation is not visible through this one.
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.EndAffiliationAsync(other.Id, affiliation.Id, None));
            Assert.Empty((await service.GetStaffAsync(staff.Id, None)).ActiveClinics);

            var back = await service.StartAffiliationAsync(staff.Id, new StartAffiliationRequest(clinic.Id), None);
            var history = await service.ListAffiliationsAsync(staff.Id, None);
            Assert.Equal([back.Id, affiliation.Id], history.Select(a => a.Id));
            Assert.Equal([true, false], history.Select(a => a.IsActive));
            Assert.Equal(2, store.Affiliations.Count);
        }

        [Fact]
        public async Task DeactivatingAClinicOrStaffDoesNotEndTheirAffiliations()
        {
            var (service, store, _) = Create();
            var clinic = await service.RegisterClinicAsync(Clinic(), None);
            var staff = await service.RegisterStaffAsync(Doctor(), None);
            await service.StartAffiliationAsync(staff.Id, new StartAffiliationRequest(clinic.Id), None);

            await service.SetClinicActiveAsync(clinic.Id, false, None);
            await service.SetStaffActiveAsync(staff.Id, false, None);

            Assert.All(store.Affiliations, a => Assert.True(a.IsActive));
        }

        // In-memory organization store: writes become visible only after SaveChangesAsync.
        private sealed class FakeOrganizationStore : IClinicalOrganizationStore
        {
            private readonly List<object> _pending = [];

            public List<Clinic> Clinics { get; } = [];
            public List<ClinicalStaff> Staff { get; } = [];
            public List<ClinicStaffAffiliation> Affiliations { get; } = [];
            public List<ClinicalAuditEntry> Audit { get; } = [];

            public Task<IReadOnlyList<ClinicAdminSummary>> ListClinicsAsync(string? search, ClinicStatus? status, CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyList<ClinicAdminSummary>>(Clinics
                    .Where(c => status is null || c.Status == status)
                    .Where(c => search is null || c.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(c => c.Name, StringComparer.Ordinal).Select(ToSummary).ToList());

            public Task<ClinicAdminSummary?> GetClinicAsync(Guid clinicId, CancellationToken cancellationToken) =>
                Task.FromResult(Clinics.SingleOrDefault(c => c.Id == clinicId) is { } c ? ToSummary(c) : null);

            public Task<Clinic?> FindClinicAsync(Guid clinicId, CancellationToken cancellationToken) =>
                Task.FromResult(Clinics.SingleOrDefault(c => c.Id == clinicId));

            public Task<IReadOnlyList<ClinicalStaffAdminSummary>> ListStaffAsync(string? search, StaffStatus? status, CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyList<ClinicalStaffAdminSummary>>(Staff
                    .Where(s => status is null || s.Status == status)
                    .Where(s => search is null || $"{s.GivenNames} {s.FamilyNames}".Contains(search, StringComparison.OrdinalIgnoreCase))
                    .Select(ToSummary).ToList());

            public Task<ClinicalStaffAdminSummary?> GetStaffAsync(Guid staffId, CancellationToken cancellationToken) =>
                Task.FromResult(Staff.SingleOrDefault(s => s.Id == staffId) is { } s ? ToSummary(s) : null);

            public Task<ClinicalStaff?> FindStaffAsync(Guid staffId, CancellationToken cancellationToken) =>
                Task.FromResult(Staff.SingleOrDefault(s => s.Id == staffId));

            public Task<IReadOnlyList<ClinicStaffAffiliationSummary>> ListAffiliationsAsync(Guid staffId, CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyList<ClinicStaffAffiliationSummary>>(Affiliations
                    .Where(a => a.ClinicalStaffId == staffId)
                    .OrderBy(a => !a.IsActive).ThenByDescending(a => a.StartedAtUtc)
                    .Select(a =>
                    {
                        var clinic = Clinics.Single(c => c.Id == a.ClinicId);
                        return new ClinicStaffAffiliationSummary(a.Id, clinic.Id, clinic.Name, clinic.Status, a.StartedAtUtc, a.EndedAtUtc, a.IsActive);
                    })
                    .ToList());

            public Task<bool> HasActiveAffiliationAsync(Guid staffId, Guid clinicId, CancellationToken cancellationToken) =>
                Task.FromResult(Affiliations.Any(a => a.ClinicalStaffId == staffId && a.ClinicId == clinicId && a.IsActive));

            public Task<ClinicStaffAffiliation?> FindAffiliationAsync(Guid staffId, Guid affiliationId, CancellationToken cancellationToken) =>
                Task.FromResult(Affiliations.SingleOrDefault(a => a.Id == affiliationId && a.ClinicalStaffId == staffId));

            public void Add(Clinic clinic) => _pending.Add(clinic);
            public void Add(ClinicalStaff staff) => _pending.Add(staff);
            public void Add(ClinicStaffAffiliation affiliation) => _pending.Add(affiliation);
            public void Add(ClinicalAuditEntry auditEntry) => _pending.Add(auditEntry);

            public Task SaveChangesAsync(CancellationToken cancellationToken)
            {
                Clinics.AddRange(_pending.OfType<Clinic>());
                Staff.AddRange(_pending.OfType<ClinicalStaff>());
                Affiliations.AddRange(_pending.OfType<ClinicStaffAffiliation>());
                Audit.AddRange(_pending.OfType<ClinicalAuditEntry>());
                _pending.Clear();
                return Task.CompletedTask;
            }

            private ClinicAdminSummary ToSummary(Clinic c) => new(
                c.Id, c.Name, c.Type, c.FacilityCode, c.Province, c.Municipality, c.Address, c.PhoneNumber, c.Status,
                Affiliations.Count(a => a.ClinicId == c.Id && a.IsActive));

            private ClinicalStaffAdminSummary ToSummary(ClinicalStaff s) => new(
                s.Id, s.GivenNames, s.FamilyNames, s.Type, s.Status, s.ProfessionalLicenseNumber, s.Specialty, s.PhoneNumber,
                s.IdentityUserId is not null, s.IdentityUserId,
                Affiliations.Where(a => a.ClinicalStaffId == s.Id && a.IsActive)
                    .Select(a => Clinics.Single(c => c.Id == a.ClinicId))
                    .OrderBy(c => c.Name, StringComparer.Ordinal)
                    .Select(c => new ClinicSummary(c.Id, c.Name)).ToList());
        }
    }
}
