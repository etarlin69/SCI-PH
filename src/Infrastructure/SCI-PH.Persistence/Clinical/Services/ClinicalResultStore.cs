using Microsoft.EntityFrameworkCore;
using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Persistence.Contexts;

namespace SCI_PH.Persistence.Clinical.Services
{
    internal sealed class ClinicalResultStore : IClinicalResultStore
    {
        private readonly ClinicalDbContext _clinical;

        public ClinicalResultStore(ClinicalDbContext clinical) => _clinical = clinical;

        public async Task<IReadOnlyList<ClinicalResultSummary>> ListForEncounterAsync(Guid encounterId, CancellationToken cancellationToken) =>
            await _clinical.ClinicalResults.AsNoTracking()
                .Where(r => r.ClinicalEncounterId == encounterId)
                .OrderByDescending(r => r.ResultedAtUtc.HasValue).ThenByDescending(r => r.ResultedAtUtc).ThenBy(r => r.Name).ThenBy(r => r.Id)
                .Select(r => new ClinicalResultSummary(
                    r.Id, r.ClinicalEncounterId, r.Type, r.Status, r.Name, r.CodeSystem, r.Code, r.ResultText, r.NumericValue, r.Unit,
                    r.ReferenceRange, r.ResultedAtUtc,
                    _clinical.ClinicalStaffMembers.Where(s => s.Id == r.RecordedByStaffId)
                        .Select(s => s.GivenNames + " " + s.FamilyNames).FirstOrDefault()))
                .ToListAsync(cancellationToken);

        public Task<ClinicalResult?> FindForEncounterAsync(Guid encounterId, Guid resultId, CancellationToken cancellationToken) =>
            _clinical.ClinicalResults.AsNoTracking()
                .SingleOrDefaultAsync(r => r.Id == resultId && r.ClinicalEncounterId == encounterId, cancellationToken);

        public void Add(ClinicalResult result) => _clinical.ClinicalResults.Add(result);

        // ClinicalResult has no concurrency token (no schema change for it): the transition is a conditional UPDATE inside the
        // same transaction as the rest of the unit of work, so a lost race writes nothing at all.
        public async Task SaveTransitionAsync(ClinicalResult result, ClinicalResultStatus expectedStatus, CancellationToken cancellationToken)
        {
            await using var transaction = await _clinical.Database.BeginTransactionAsync(cancellationToken);
            var updated = await _clinical.ClinicalResults
                .Where(r => r.Id == result.Id && r.Status == expectedStatus)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(r => r.Status, result.Status)
                    .SetProperty(r => r.ResultText, result.ResultText)
                    .SetProperty(r => r.NumericValue, result.NumericValue)
                    .SetProperty(r => r.Unit, result.Unit)
                    .SetProperty(r => r.ResultedAtUtc, result.ResultedAtUtc)
                    .SetProperty(r => r.RecordedByStaffId, result.RecordedByStaffId), cancellationToken);
            if (updated != 1)
            {
                _clinical.ChangeTracker.Clear();
                throw new ResourceConflictException("The result was changed by another operation. Reload it and try again.");
            }

            try
            {
                await _clinical.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException exception)
            {
                _clinical.ChangeTracker.Clear();
                throw new ResourceConflictException("The record was changed by another operation. Reload it and try again.", exception);
            }

            await transaction.CommitAsync(cancellationToken);
        }
    }
}
