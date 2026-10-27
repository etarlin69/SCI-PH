using System.Text.Json.Serialization;
using SCI_PH.Application.Common.Serialization;
using SCI_PH.Domain.Enums.Patients;

namespace SCI_PH.Application.DTOs.Clinical
{
    // Clinical contracts: enums travel as text (TextEnumConverter) and unknown members are rejected.

    // Initial identifier entered at registration. Normalization is done by the Domain, never trusted from the client.
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PatientIdentifierInput(
        [property: JsonRequired, JsonConverter(typeof(TextEnumConverter))] PatientIdentifierType Type,
        string Value,
        string? Issuer = null);

    // Mirrors Patient.Register(givenNames, familyNames, dateOfBirth, sex) plus an optional first identifier.
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record RegisterPatientRequest(
        string GivenNames,
        string FamilyNames,
        DateOnly? DateOfBirth,
        [property: JsonRequired, JsonConverter(typeof(TextEnumConverter))] BiologicalSex Sex,
        PatientIdentifierInput? Identifier = null);

    // Sent in the body so national ids or passports never appear in URLs or access logs.
    // With IdentifierType: exact match on the normalized value. Without it: name search (3+ characters).
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PatientSearchRequest(
        [property: JsonConverter(typeof(TextEnumConverter))] PatientIdentifierType? IdentifierType,
        string Value);

    // Value is masked except for internal SCI-PH codes.
    public sealed record PatientIdentifierSummary(
        [property: JsonConverter(typeof(TextEnumConverter))] PatientIdentifierType Type,
        string Value);

    public sealed record PatientSummary(
        Guid Id,
        string DisplayName,
        DateOnly? BirthDate,
        [property: JsonConverter(typeof(TextEnumConverter))] BiologicalSex BiologicalSex,
        [property: JsonConverter(typeof(TextEnumConverter))] PatientIdentityStatus IdentityStatus,
        PatientIdentifierSummary? PrimaryIdentifier);

    // Read-store row. Identifier holds the normalized value; masking happens in Application before it leaves.
    public sealed record PatientRow(
        Guid Id,
        string GivenNames,
        string FamilyNames,
        DateOnly? DateOfBirth,
        BiologicalSex Sex,
        PatientIdentityStatus IdentityStatus,
        PatientIdentifierRow? PrimaryIdentifier);

    public sealed record PatientIdentifierRow(PatientIdentifierType Type, string NormalizedValue);

    // A patient's biometric reference; never its provider subject reference outside the read store.
    public sealed record BiometricReferenceRow(Guid Id, Guid PatientId);
}
