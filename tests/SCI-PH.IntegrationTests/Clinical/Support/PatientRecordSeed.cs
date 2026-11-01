using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Entities.Medications;
using SCI_PH.Domain.Entities.Organizations;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Enums.Medications;
using SCI_PH.Domain.Enums.Organizations;
using SCI_PH.Domain.Enums.Patients;
using SCI_PH.Persistence.Contexts;

namespace SCI_PH.IntegrationTests.Clinical.Support
{
    // Ids of what a patient should (and should not) see of their own record.
    internal sealed record SeededPatientRecord(
        Guid PatientId,
        string GivenNames,
        Guid EncounterId,
        Guid OlderEncounterId,
        Guid DiagnosisId,
        Guid AllergyId,
        Guid SupersededAllergyId,
        Guid FinalResultId,
        Guid PendingResultId,
        Guid IssuedPrescriptionId,
        Guid DraftPrescriptionId,
        Guid CancelledDraftPrescriptionId);

    // A complete own record for a patient: two encounters in a clinic, a diagnosis, two medical history versions (only
    // the latest allergy is current), a published and a pending result, and an issued, a draft and a cancelled-draft
    // prescription. Works on the in-memory stores and on PostgreSQL (real clinic, staff and medication rows).
    internal static class PatientRecordSeed
    {
        public static async Task<SeededPatientRecord> SeedAsync(
            ClinicalDbContext db, string givenNames, Guid? identityUserId, string? email = null)
        {
            var now = DateTimeOffset.UtcNow;
            var clinic = Clinic.Register($"Hospital {givenNames}", ClinicType.Hospital, "Santo Domingo", "Distrito Nacional");
            var staff = ClinicalStaff.Register("Doctora", givenNames, ClinicalStaffType.Physician);
            var medication = Medication.Register($"Amoxicilina {givenNames}", "Cápsula", "500 mg", $"SELF-{Guid.NewGuid():N}"[..24], MedicationRoute.Oral);
            db.AddRange(clinic, staff, medication);

            var patient = Patient.Register(givenNames, "Prueba", new DateOnly(1990, 5, 20), BiologicalSex.Female);
            patient.AddIdentifier(PatientIdentifierType.NationalId, ClinicalApiHost.RandomNationalId(), PatientIdentifierSource.Manual, isPrimary: true);
            patient.AddIdentifier(
                PatientIdentifierType.Email, email ?? $"{givenNames.ToLowerInvariant()}-{Guid.NewGuid():N}@sci-ph.test", PatientIdentifierSource.Manual, isPrimary: true);
            if (identityUserId is { } userId)
            {
                patient.LinkIdentityUser(userId);
            }

            db.Patients.Add(patient);

            var older = ClinicalEncounter.CreateForPatient(patient.Id, ClinicalEncounterType.Consultation, now.AddDays(-30), clinic.Id, reason: "Control");
            var encounter = ClinicalEncounter.CreateForPatient(patient.Id, ClinicalEncounterType.Consultation, now.AddHours(-3), clinic.Id, reason: $"Fiebre {givenNames}");
            db.ClinicalEncounters.AddRange(older, encounter);

            var diagnosis = Diagnosis.Record(encounter.Id, staff.Id, $"Faringitis {givenNames}", DiagnosisType.Definitive, now.AddHours(-2), isPrimary: true);
            db.Diagnoses.Add(diagnosis);

            var firstHistory = PatientMedicalHistory.Record(patient.Id, 1, staff.Id);
            var superseded = firstHistory.AddAllergy($"Polen {givenNames}", AllergySeverity.Mild, ClinicalRecordStatus.Active);
            var currentHistory = PatientMedicalHistory.Record(patient.Id, 2, staff.Id);
            var allergy = currentHistory.AddAllergy($"Penicilina {givenNames}", AllergySeverity.Severe, ClinicalRecordStatus.Active, "Urticaria");
            db.PatientMedicalHistories.AddRange(firstHistory, currentHistory);

            var final = ClinicalResult.Request(encounter.Id, ClinicalResultType.Laboratory, $"Hemoglobina {givenNames}", referenceRange: "12-16 g/dL");
            final.RecordFinal(now.AddHours(-1), numericValue: 13.5m, unit: "g/dL", recordedByStaffId: staff.Id);
            var pending = ClinicalResult.Request(encounter.Id, ClinicalResultType.Imaging, $"Radiografía {givenNames}");
            db.ClinicalResults.AddRange(final, pending);

            var issued = Prescription.Draft(encounter.Id, staff.Id, "Tomar con alimentos");
            issued.AddDetail(medication.Id, 500, "mg", MedicationRoute.Oral, "Cada 8 horas", 7);
            issued.Issue(now.AddHours(-2));
            var draft = Prescription.Draft(encounter.Id, staff.Id);
            draft.AddDetail(medication.Id, 250, "mg", MedicationRoute.Oral, "Cada 12 horas");
            var cancelledDraft = Prescription.Draft(encounter.Id, staff.Id);
            cancelledDraft.AddDetail(medication.Id, 1, "g", MedicationRoute.Oral, "Una vez");
            cancelledDraft.Cancel(now.AddHours(-1));
            db.Prescriptions.AddRange(issued, draft, cancelledDraft);

            await db.SaveChangesAsync();
            return new SeededPatientRecord(
                patient.Id, givenNames, encounter.Id, older.Id, diagnosis.Id, allergy.Id, superseded.Id, final.Id, pending.Id,
                issued.Id, draft.Id, cancelledDraft.Id);
        }
    }
}
