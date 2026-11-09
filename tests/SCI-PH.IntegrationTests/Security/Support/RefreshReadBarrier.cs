using Microsoft.Extensions.DependencyInjection;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;

namespace SCI_PH.IntegrationTests.Security.Support
{
    // Makes the lost-rotation race deterministic: once armed for N participants, the next N refresh-token reads wait for
    // each other AFTER reading, so all of them see the token as active before any of them tries to rotate it. Exactly
    // the situation of two browser tabs sending the same cookie at the same time. Unarmed, it changes nothing.
    internal sealed class RefreshReadBarrier
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

        private int _remaining;
        private TaskCompletionSource _open = NewGate();

        public void Arm(int participants)
        {
            _open = NewGate();
            Volatile.Write(ref _remaining, participants);
        }

        public async Task WaitAsync(CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref _remaining) <= 0)
            {
                return;
            }

            var gate = _open;
            if (Interlocked.Decrement(ref _remaining) == 0)
            {
                gate.TrySetResult();
            }

            await gate.Task.WaitAsync(Timeout, cancellationToken);
        }

        private static TaskCompletionSource NewGate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    internal sealed class BarrierRefreshTokenStore : IRefreshTokenStore
    {
        private readonly IRefreshTokenStore _inner;
        private readonly RefreshReadBarrier _barrier;

        public BarrierRefreshTokenStore(IRefreshTokenStore inner, RefreshReadBarrier barrier)
        {
            _inner = inner;
            _barrier = barrier;
        }

        public Task AddAsync(NewRefreshToken token, CancellationToken cancellationToken) => _inner.AddAsync(token, cancellationToken);

        public async Task<StoredRefreshToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken)
        {
            var stored = await _inner.FindByHashAsync(tokenHash, cancellationToken);
            await _barrier.WaitAsync(cancellationToken);
            return stored;
        }

        public Task<RefreshTokenRotationResult> RotateAsync(
            Guid currentTokenId, NewRefreshToken replacement, string? ipAddress, CancellationToken cancellationToken) =>
            _inner.RotateAsync(currentTokenId, replacement, ipAddress, cancellationToken);

        public Task<int> RevokeFamilyAsync(Guid familyId, RefreshTokenRevocationReason reason, string? ipAddress, CancellationToken cancellationToken) =>
            _inner.RevokeFamilyAsync(familyId, reason, ipAddress, cancellationToken);

        public Task<int> RevokeAllForUserAsync(Guid userId, RefreshTokenRevocationReason reason, string? ipAddress, CancellationToken cancellationToken) =>
            _inner.RevokeAllForUserAsync(userId, reason, ipAddress, cancellationToken);
    }

    internal static class RefreshReadBarrierRegistration
    {
        // Wraps the real IRefreshTokenStore registration (same lifetime) with the barrier.
        public static IServiceCollection AddRefreshReadBarrier(this IServiceCollection services, RefreshReadBarrier barrier)
        {
            var real = services.Last(d => d.ServiceType == typeof(IRefreshTokenStore));
            var implementation = real.ImplementationType
                ?? throw new InvalidOperationException("IRefreshTokenStore is expected to be registered by type.");
            services.Remove(real);
            services.Add(new ServiceDescriptor(
                typeof(IRefreshTokenStore),
                sp => new BarrierRefreshTokenStore((IRefreshTokenStore)ActivatorUtilities.CreateInstance(sp, implementation), barrier),
                real.Lifetime));
            return services;
        }
    }
}
