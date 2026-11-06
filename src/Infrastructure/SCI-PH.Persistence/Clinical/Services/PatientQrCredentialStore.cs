using Microsoft.EntityFrameworkCore;
using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Patients;
using SCI_PH.Persistence.Contexts;

namespace SCI_PH.Persistence.Clinical.Services
{
    internal sealed class PatientQrCredentialStore : IPatientQrCredentialStore
    {
        private readonly ClinicalDbContext _clinical;

        public PatientQrCredentialStore(ClinicalDbContext clinical) => _clinical = clinical;

        public Task<Patient?> LoadPatientWithActiveQrAsync(Guid patientId, CancellationToken cancellationToken) =>
            _clinical.Patients
                .Include(p => p.Identifiers.Where(i => i.Type == PatientIdentifierType.QrCode && i.Status == PatientIdentifierStatus.Active))
                .SingleOrDefaultAsync(p => p.Id == patientId, cancellationToken);

        // Explicit: EF would otherwise take an unknown child of a tracked patient, with its key already set, for an update.
        public void Add(PatientIdentifier credential) => _clinical.PatientIdentifiers.Add(credential);

        // The hash of a revoked credential may repeat only in theory (256 random bits): the active one, if any, wins.
        public Task<PatientIdentifier?> FindByHashAsync(string hash, CancellationToken cancellationToken) =>
            _clinical.PatientIdentifiers.AsNoTracking()
                .Where(i => i.Type == PatientIdentifierType.QrCode
                    && i.Issuer == string.Empty
                    && i.Source == PatientIdentifierSource.System
                    && i.NormalizedValue == hash)
                .OrderBy(i => i.Status)
                .FirstOrDefaultAsync(cancellationToken);
    }
}
