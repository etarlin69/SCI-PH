using System.Text.Json.Serialization;
using SCI_PH.Application.Common.Serialization;
using SCI_PH.Domain.Enums.Organizations;

namespace SCI_PH.Application.DTOs.Clinical
{
    // Clinic.Register: exactly the Domain fields. There is no update or delete in the Domain.
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record RegisterClinicRequest(
        string Name,
        [property: JsonRequired, JsonConverter(typeof(TextEnumConverter))] ClinicType Type,
        string Province,
        string Municipality,
        string? FacilityCode = null,
        string? Address = null,
        string? PhoneNumber = null);

    public sealed record ClinicAdminSummary(
        Guid Id,
        string Name,
        [property: JsonConverter(typeof(TextEnumConverter))] ClinicType Type,
        string? FacilityCode,
        string Province,
        string Municipality,
        string? Address,
        string? PhoneNumber,
        [property: JsonConverter(typeof(TextEnumConverter))] ClinicStatus Status,
        // Staff with an open affiliation to the clinic.
        int ActiveStaffCount);

    // ClinicalStaff.Register. The staff member may exist without an account; the account is created by the staff
    // invitation (Users.Manage) and linked on activation. There is no email here: it belongs to the invitation.
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record RegisterClinicalStaffRequest(
        string GivenNames,
        string FamilyNames,
        [property: JsonRequired, JsonConverter(typeof(TextEnumConverter))] ClinicalStaffType Type,
        string? ProfessionalLicenseNumber = null,
        string? Specialty = null,
        string? PhoneNumber = null);

    public sealed record ClinicalStaffAdminSummary(
        Guid Id,
        string GivenNames,
        string FamilyNames,
        [property: JsonConverter(typeof(TextEnumConverter))] ClinicalStaffType Type,
        [property: JsonConverter(typeof(TextEnumConverter))] StaffStatus Status,
        string? ProfessionalLicenseNumber,
        string? Specialty,
        string? PhoneNumber,
        // Only whether an account is linked (ClinicalStaff.IdentityUserId); account data stays in Identity (Users.Manage).
        bool AccountLinked,
        Guid? IdentityUserId,
        // Clinics with an open affiliation.
        IReadOnlyList<ClinicSummary> ActiveClinics);

    public sealed record ClinicStaffAffiliationSummary(
        Guid Id,
        Guid ClinicId,
        string ClinicName,
        [property: JsonConverter(typeof(TextEnumConverter))] ClinicStatus ClinicStatus,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset? EndedAtUtc,
        bool IsActive);

    // ClinicStaffAffiliation.Start at the current time.
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record StartAffiliationRequest([property: JsonRequired] Guid ClinicId);
}
