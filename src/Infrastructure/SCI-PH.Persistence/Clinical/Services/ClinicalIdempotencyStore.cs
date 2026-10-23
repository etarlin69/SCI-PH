using Microsoft.EntityFrameworkCore;
using SCI_PH.Application.Abstractions.Idempotency;
using SCI_PH.Persistence.Clinical.Idempotency;
using SCI_PH.Persistence.Contexts;

namespace SCI_PH.Persistence.Clinical.Services
{
    // Receipts share the scoped ClinicalDbContext with ClinicalWriteStore: Add only tracks the receipt and
    // ClinicalWriteStore.SaveChangesAsync commits it together with the clinical rows (one transaction).
    internal sealed class ClinicalIdempotencyStore : IIdempotencyStore
    {
        private readonly ClinicalDbContext _clinical;

        public ClinicalIdempotencyStore(ClinicalDbContext clinical) => _clinical = clinical;

        public async Task<IdempotencyReceipt?> FindAsync(IdempotencyKey key, CancellationToken cancellationToken) =>
            (await _clinical.IdempotencyReceipts.AsNoTracking()
                .SingleOrDefaultAsync(r => r.ActorUserId == key.ActorUserId
                    && r.Operation == key.Operation
                    && r.ClientRequestId == key.ClientRequestId, cancellationToken))
            ?.ToReceipt();

        public void Add(IdempotencyReceipt receipt) => _clinical.IdempotencyReceipts.Add(IdempotencyReceiptEntry.From(receipt));
    }
}
