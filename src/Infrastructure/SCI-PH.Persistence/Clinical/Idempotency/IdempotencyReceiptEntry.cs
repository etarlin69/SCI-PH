using SCI_PH.Application.Abstractions.Idempotency;

namespace SCI_PH.Persistence.Clinical.Idempotency
{
    // Technical persistence state, NOT a Clinical Domain entity: it records that a create request of one user was already
    // executed, so a retry returns the same resource (IIdempotencyStore). It lives in the clinical schema only so it is
    // committed in the same transaction as the clinical rows it protects. No foreign key: ResourceId may point to the
    // resource of any operation, and receipts never block the lifecycle of clinical data.
    public sealed class IdempotencyReceiptEntry
    {
        public const int OperationMaxLength = 100;

        private IdempotencyReceiptEntry() { }

        public Guid Id { get; private set; }
        public Guid ActorUserId { get; private set; }
        // Server-controlled, e.g. "prehospital-emergency.open.v1"; never taken from the client.
        public string Operation { get; private set; } = string.Empty;
        public Guid ClientRequestId { get; private set; }
        // SHA-256 (hex) of the logical payload; see IdempotencyRequestHash.
        public string RequestHash { get; private set; } = string.Empty;
        public Guid ResourceId { get; private set; }
        public DateTimeOffset CreatedAtUtc { get; private set; }

        public static IdempotencyReceiptEntry From(IdempotencyReceipt receipt) =>
            new()
            {
                Id = Guid.NewGuid(),
                ActorUserId = receipt.Key.ActorUserId,
                Operation = receipt.Key.Operation,
                ClientRequestId = receipt.Key.ClientRequestId,
                RequestHash = receipt.RequestHash,
                ResourceId = receipt.ResourceId,
                CreatedAtUtc = receipt.CreatedAtUtc
            };

        public IdempotencyReceipt ToReceipt() =>
            new(new IdempotencyKey(ActorUserId, Operation, ClientRequestId), RequestHash, ResourceId, CreatedAtUtc);
    }
}
