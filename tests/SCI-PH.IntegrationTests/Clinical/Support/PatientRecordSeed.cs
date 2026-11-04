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
        Guid CorrectedResultId,
        Guid PendingResultId,
        Guid IssuedPrescriptionId,
        Guid IssuedThenCancelledPrescriptionId,
        Guid DraftPrescriptionId,
        Guid CancelledDraftPrescriptionId,
        Guid PresumptiveDiagnosisId,
        Guid InProgressDiagnosisId,
        Guid CancelledEncounterId,
        Guid CancelledDiagnosisId,
        Guid CancelledResultId,
        Guid CancelledPrescriptionId);

    // A complete own record for a patient: a closed encounter, one in progress and a cancelled one in a clinic; diagnoses
    // definitive and presumptive (only the definitive one of the closed encounter is published); two medical history
    // versions (only the latest allergy is current); a final, a corrected and a pending result; and four prescriptions:
    // issued, issued and then cancelled (still history), a draft and a draft cancelled before issue (never orders). The
    // cancelled encounter also holds a diagnosis, a result and a prescription that stay withheld. A clinical note, a vital
    // sign and a procedure exist and are never published. Works on the in-memory stores and on PostgreSQL (real clinic,
    // staff and medication rows).
    internal static class PatientRecordSeed
    {
        public const string ReasonMarker = "MotivoInterno";
        public const string PrescriptionNoteMarker = "NotaInternaReceta";
        public const string ClinicalNoteMarker = "NotaClinicaInterna";
        public const string ProcedureMarker = "ProcedimientoInterno";

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

            var older = ClinicalEncounter.CreateForPatient(patient.Id, ClinicalEncounterType.Consultation, now.AddDays(-30), clinic.Id, reason: $"{ReasonMarker} control");
            older.ChangeStatus(ClinicalEncounterStatus.InProgress, now.AddDays(-30));
            var encounter = ClinicalEncounter.CreateForPatient(patient.Id, ClinicalEncounterType.Consultation, now.AddHours(-3), clinic.Id, reason: $"{ReasonMarker} {givenNames}");
            encounter.ChangeStatus(ClinicalEncounterStatus.InProgress, now.AddHours(-3));
            encounter.ChangeStatus(ClinicalEncounterStatus.Closed, now.AddMinutes(-5));
            var cancelledEncounter = ClinicalEncounter.CreateForPatient(patient.Id, ClinicalEncounterType.Consultation, now.AddDays(-10), clinic.Id, reason: $"{ReasonMarker} cancelada");
            cancelledEncounter.ChangeStatus(ClinicalEncounterStatus.Cancelled, now.AddDays(-10));
            db.ClinicalEncounters.AddRange(older, encounter, cancelledEncounter);

            var diagnosis = Diagnosis.Record(encounter.Id, staff.Id, $"Faringitis {givenNames}", DiagnosisType.Definitive, now.AddHours(-2), isPrimary: true);
            var presumptive = Diagnosis.Record(encounter.Id, staff.Id, $"Sospecha {givenNames}", DiagnosisType.Presumptive, now.AddHours(-2.5));
            var inProgressDiagnosis = Diagnosis.Record(older.Id, staff.Id, $"Abierto {givenNames}", DiagnosisType.Definitive, now.AddDays(-29));
            var cancelledDiagnosis = Diagnosis.Record(cancelledEncounter.Id, staff.Id, $"Cancelado {givenNames}", DiagnosisType.Definitive, now.AddDays(-10));
            db.Diagnoses.AddRange(diagnosis, presumptive, inProgressDiagnosis, cancelledDiagnosis);

            db.ClinicalNotes.Add(ClinicalNote.Write(encounter.Id, staff.Id, ClinicalNoteType.Discharge, $"{ClinicalNoteMarker} {givenNames}", now.AddHours(-1)));
            db.VitalSigns.Add(VitalSign.Record(encounter.Id, VitalSignType.HeartRate, 72, now.AddHours(-1), staff.Id));
            db.ClinicalProcedures.Add(ClinicalProcedure.RecordCompleted(encounter.Id, $"{ProcedureMarker} {givenNames}", staff.Id, now.AddHours(-1), now.AddMinutes(-50)));

            var firstHistory = PatientMedicalHistory.Record(patient.Id, 1, staff.Id);
            var superseded = firstHistory.AddAllergy($"Polen {givenNames}", AllergySeverity.Mild, ClinicalRecordStatus.Active);
            var currentHistory = PatientMedicalHistory.Record(patient.Id, 2, staff.Id);
            var allergy = currentHistory.AddAllergy($"Penicilina {givenNames}", AllergySeverity.Severe, ClinicalRecordStatus.Active, "Urticaria");
            db.PatientMedicalHistories.AddRange(firstHistory, currentHistory);

            var final = ClinicalResult.Request(encounter.Id, ClinicalResultType.Laboratory, $"Hemoglobina {givenNames}", referenceRange: "12-16 g/dL");
            final.RecordFinal(now.AddHours(-1), numericValue: 13.5m, unit: "g/dL", recordedByStaffId: staff.Id);
            var corrected = ClinicalResult.Request(encounter.Id, ClinicalResultType.Laboratory, $"Glucosa {givenNames}");
            corrected.RecordFinal(now.AddHours(-2), numericValue: 180m, unit: "mg/dL", recordedByStaffId: staff.Id);
            corrected.Correct(now.AddHours(-1.5), numericValue: 92m, unit: "mg/dL", recordedByStaffId: staff.Id);
            var pending = ClinicalResult.Request(encounter.Id, ClinicalResultType.Imaging, $"Radiografía {givenNames}");
            var cancelledResult = ClinicalResult.Request(cancelledEncounter.Id, ClinicalResultType.Laboratory, $"Retirado {givenNames}");
            cancelledResult.RecordFinal(now.AddDays(-10), numericValue: 1m, unit: "mg/dL", recordedByStaffId: staff.Id);
            db.ClinicalResults.AddRange(final, corrected, pending, cancelledResult);

            var issued = Prescription.Draft(encounter.Id, staff.Id, $"{PrescriptionNoteMarker} {givenNames}");
            issued.AddDetail(medication.Id, 500, "mg", MedicationRoute.Oral, "Cada 8 horas", 7);
            issued.Issue(now.AddHours(-2));
            var issuedThenCancelled = Prescription.Draft(encounter.Id, staff.Id);
            issuedThenCancelled.AddDetail(medication.Id, 400, "mg", MedicationRoute.Oral, "Cada 6 horas", 3);
            issuedThenCancelled.Issue(now.AddHours(-2.5));
            issuedThenCancelled.Cancel(now.AddHours(-1.5));
            var draft = Prescription.Draft(encounter.Id, staff.Id);
            draft.AddDetail(medication.Id, 250, "mg", MedicationRoute.Oral, "Cada 12 horas");
            var cancelledDraft = Prescription.Draft(encounter.Id, staff.Id);
            cancelledDraft.AddDetail(medication.Id, 1, "g", MedicationRoute.Oral, "Una vez");
            cancelledDraft.Cancel(now.AddHours(-1));
            var cancelledPrescription = Prescription.Draft(cancelledEncounter.Id, staff.Id, PrescriptionNoteMarker);
            cancelledPrescription.AddDetail(medication.Id, 100, "mg", MedicationRoute.Oral, "Una vez");
            cancelledPrescription.Issue(now.AddDays(-10));
            db.Prescriptions.AddRange(issued, issuedThenCancelled, draft, cancelledDraft, cancelledPrescription);

            await db.SaveChangesAsync();
            return new SeededPatientRecord(
                patient.Id, givenNames, encounter.Id, older.Id, diagnosis.Id, allergy.Id, superseded.Id, final.Id, corrected.Id, pending.Id,
                issued.Id, issuedThenCancelled.Id, draft.Id, cancelledDraft.Id,
                presumptive.Id, inProgressDiagnosis.Id, cancelledEncounter.Id, cancelledDiagnosis.Id, cancelledResult.Id, cancelledPrescription.Id);
        }
    }
}
