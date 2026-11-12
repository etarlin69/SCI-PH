using SCI_PH.Domain.Entities.Patients;

namespace SCI_PH.Application.Abstractions.Clinical
{
    // QR credentials of a patient (ADR-015): PatientIdentifier rows of type QrCode and source System whose value is the hash
    // of a system-issued token. Shares the unit of work of IClinicalWriteStore: its SaveChangesAsync commits them.
    public interface IPatientQrCredentialStore
    {
        // The patient, tracked, with ONLY its active QrCode identifiers loaded. Null if the patient does not exist.
        Task<Patient?> LoadPatientWithActiveQrAsync(Guid patientId, CancellationToken cancellationToken);

        // Tracks a credential added to an already loaded patient as a new row.
        void Add(PatientIdentifier credential);

        // The system-issued credential with exactly this hash, in any status and whether expired or not (no tracking).
        // A value registered by any other means is never returned.
        Task<PatientIdentifier?> FindByHashAsync(string hash, CancellationToken cancellationToken);
    }
}
