using Microsoft.Extensions.DependencyInjection;
using SCI_PH.Application.Abstractions.Idempotency;

namespace SCI_PH.IntegrationTests.Clinical.Support
{
    // Makes the same-key race of an idempotent operation deterministic: once armed for one (operation, clientRequestId), the
    // FIRST receipt lookup of that key holds its answer (read before waiting) until the test releases it. The test can then
    // let the same request run to completion in between, so the held request continues with a stale "no receipt" exactly
    // as the loser of a double submit does. Every other lookup, and every lookup once the gate has fired, passes through.
    internal sealed class ReceiptLookupGate
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

        private Target? _armed;
        private TaskCompletionSource _reached = NewGate();
        private TaskCompletionSource _release = NewGate();

        // Completes when the held lookup has read the receipt and is waiting.
        public Task Reached => _reached.Task.WaitAsync(Timeout);

        public void Arm(string operation, Guid clientRequestId)
        {
            _reached = NewGate();
            _release = NewGate();
            Volatile.Write(ref _armed, new Target(operation, clientRequestId));
        }

        public void Release() => _release.TrySetResult();

        public async Task AfterLookupAsync(IdempotencyKey key, CancellationToken cancellationToken)
        {
            var armed = Volatile.Read(ref _armed);
            if (armed is null || key.Operation != armed.Operation || key.ClientRequestId != armed.ClientRequestId
                || !ReferenceEquals(Interlocked.CompareExchange(ref _armed, null, armed), armed))
            {
                return;
            }

            _reached.TrySetResult();
            await _release.Task.WaitAsync(Timeout, cancellationToken);
        }

        private static TaskCompletionSource NewGate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

        private sealed record Target(string Operation, Guid ClientRequestId);
    }

    internal sealed class GatedIdempotencyStore : IIdempotencyStore
    {
        private readonly IIdempotencyStore _inner;
        private readonly ReceiptLookupGate _gate;

        public GatedIdempotencyStore(IIdempotencyStore inner, ReceiptLookupGate gate)
        {
            _inner = inner;
            _gate = gate;
        }

        public async Task<IdempotencyReceipt?> FindAsync(IdempotencyKey key, CancellationToken cancellationToken)
        {
            var receipt = await _inner.FindAsync(key, cancellationToken);
            await _gate.AfterLookupAsync(key, cancellationToken);
            return receipt;
        }

        public void Add(IdempotencyReceipt receipt) => _inner.Add(receipt);
    }

    internal static class ReceiptLookupGateRegistration
    {
        // Wraps the real IIdempotencyStore registration (same lifetime) with the gate.
        public static IServiceCollection AddReceiptLookupGate(this IServiceCollection services, ReceiptLookupGate gate)
        {
            var real = services.Last(d => d.ServiceType == typeof(IIdempotencyStore));
            var implementation = real.ImplementationType
                ?? throw new InvalidOperationException("IIdempotencyStore is expected to be registered by type.");
            services.Remove(real);
            services.Add(new ServiceDescriptor(
                typeof(IIdempotencyStore),
                sp => new GatedIdempotencyStore((IIdempotencyStore)ActivatorUtilities.CreateInstance(sp, implementation), gate),
                real.Lifetime));
            return services;
        }
    }
}
