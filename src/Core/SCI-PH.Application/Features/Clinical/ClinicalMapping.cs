using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Patients;

namespace SCI_PH.Application.Features.Clinical
{
    internal static class ClinicalMapping
    {
        private const int VisibleIdentifierCharacters = 4;
        private const string MaskPrefix = "***";

        public static string DisplayName(PatientRow patient) => $"{patient.GivenNames} {patient.FamilyNames}";

        public static PatientSummary ToSummary(this PatientRow patient) =>
            new(
                patient.Id,
                DisplayName(patient),
                patient.DateOfBirth,
                patient.Sex,
                patient.IdentityStatus,
                patient.PrimaryIdentifier is { } identifier
                    ? new PatientIdentifierSummary(identifier.Type, Mask(identifier.Type, identifier.NormalizedValue))
                    : null);

        public static PatientRow ToRow(this Patient patient)
        {
            var identifier = patient.Identifiers
                .Where(i => i.IsActive)
                .OrderByDescending(i => i.IsPrimary)
                .ThenBy(i => i.CreatedAtUtc)
                .FirstOrDefault();
            return new PatientRow(
                patient.Id,
                patient.GivenNames,
                patient.FamilyNames,
                patient.DateOfBirth,
                patient.Sex,
                patient.IdentityStatus,
                identifier is null ? null : new PatientIdentifierRow(identifier.Type, identifier.NormalizedValue));
        }

        public static EncounterSummary ToSummary(this EncounterRow encounter) =>
            new(
                encounter.Id,
                encounter.PatientId,
                encounter.Patient?.ToSummary(),
                encounter.TemporaryPatientCode,
                encounter.ClinicId,
                encounter.Type,
                encounter.Status,
                encounter.Reason,
                encounter.StartedAtUtc,
                encounter.EndedAtUtc,
                encounter.IdentifiedAtUtc,
                encounter.LatestTriagePriority);

        public static AdmissionEncounterSummary ToAdmission(this EncounterRow encounter) =>
            new(
                encounter.Id,
                encounter.PatientId,
                encounter.Patient is { } patient ? DisplayName(patient) : null,
                encounter.TemporaryPatientCode,
                encounter.Type,
                encounter.Status,
                encounter.StartedAtUtc,
                encounter.ClinicId);

        public static TriageSummary ToSummary(this Triage triage) =>
            new(triage.Id, triage.ClinicalEncounterId, triage.Priority, triage.Reason, triage.Observations,
                triage.PerformedAtUtc, triage.PerformedByStaffId);

        public static VitalSignSummary ToSummary(this VitalSign vitalSign) =>
            new(vitalSign.Id, vitalSign.ClinicalEncounterId, vitalSign.TriageId, vitalSign.Type, vitalSign.Value,
                vitalSign.Unit, vitalSign.MeasuredAtUtc, vitalSign.MeasuredByStaffId);

        public static ClinicalNoteSummary ToSummary(this ClinicalNote note, string? authorDisplayName) =>
            new(note.Id, note.ClinicalEncounterId, note.Type, note.Content, note.OccurredAtUtc, note.CreatedAtUtc,
                note.AuthorStaffId, authorDisplayName);

        public static DiagnosisSummary ToSummary(this Diagnosis diagnosis) =>
            new(diagnosis.Id, diagnosis.ClinicalEncounterId, diagnosis.Description, diagnosis.CodeSystem, diagnosis.Code,
                diagnosis.Type, diagnosis.IsPrimary, diagnosis.RecordedAtUtc, diagnosis.ConfirmedAtUtc, diagnosis.RecordedByStaffId);

        // Personal identifiers leave the API showing only their last characters, with a fixed prefix so the
        // original length is not revealed. Internal SCI-PH codes are not personal data and are shown in full.
        public static string Mask(PatientIdentifierType type, string value)
        {
            if (type == PatientIdentifierType.InternalCode)
            {
                return value;
            }

            return value.Length <= VisibleIdentifierCharacters
                ? new string('*', VisibleIdentifierCharacters)
                : MaskPrefix + value[^VisibleIdentifierCharacters..];
        }
    }
}
