namespace SCI_PH.Application.Abstractions.Idempotency
{
    // Technical receipts of create requests that a client may retry (a lost response, a double tap). A receipt maps
    // (actor, server-controlled operation, client request id) to the resource it created; it is not clinical data.
    // Add only tracks the receipt: it is written by the next SaveChangesAsync of the write store of the same request,
    // in the same transaction as the resource, so a resource never exists without its receipt or the other way round.
    public interface IIdempotencyStore
    {
        Task<IdempotencyReceipt?> FindAsync(IdempotencyKey key, CancellationToken cancellationToken);

        void Add(IdempotencyReceipt receipt);
    }

    // Unique per actor and operation: two users, or two operations, never share a receipt.
    public sealed record IdempotencyKey(Guid ActorUserId, string Operation, Guid ClientRequestId);

    public sealed record IdempotencyReceipt(IdempotencyKey Key, string RequestHash, Guid ResourceId, DateTimeOffset CreatedAtUtc);

    // Raised by SaveChangesAsync when a concurrent request with the same key committed its receipt first. Nothing of
    // this request was written; the caller reads the winning receipt and answers as a replay or as a conflict.
    public sealed class IdempotencyKeyCollisionException : Exception
    {
        public IdempotencyKeyCollisionException(Exception innerException)
            : base("A request with the same idempotency key was committed concurrently.", innerException) { }
    }
}
