using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SCI_PH.Application.Abstractions.Storage;

namespace SCI_PH.IntegrationTests.Clinical.Support
{
    // Deterministic test double of the Storage Service (no Azure): keeps objects in memory, never overwrites, and can be
    // switched off to behave like an unreachable storage.
    internal sealed class InMemoryDocumentStorage : IStorageService
    {
        public ConcurrentDictionary<string, (byte[] Content, string ContentType)> Objects { get; } = new();

        public bool Unavailable { get; set; }

        public Task UploadAsync(string path, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken)
        {
            Fail();
            return Objects.TryAdd(path, (content.ToArray(), contentType))
                ? Task.CompletedTask
                : throw new InvalidOperationException("The object already exists.");
        }

        public Task<Stream?> OpenReadAsync(string path, CancellationToken cancellationToken)
        {
            Fail();
            return Task.FromResult<Stream?>(Objects.TryGetValue(path, out var stored) ? new MemoryStream(stored.Content, writable: false) : null);
        }

        public Task DeleteAsync(string path, CancellationToken cancellationToken)
        {
            Objects.TryRemove(path, out _);
            return Task.CompletedTask;
        }

        private void Fail()
        {
            if (Unavailable)
            {
                throw new IOException("Storage unreachable.");
            }
        }
    }

    internal static class InMemoryDocumentStorageRegistration
    {
        public static IServiceCollection UseDocumentStorage(this IServiceCollection services, IStorageService storage)
        {
            services.RemoveAll<IStorageService>();
            services.AddSingleton(storage);
            return services;
        }
    }
}
