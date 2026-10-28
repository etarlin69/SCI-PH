using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using SCI_PH.Infrastructure.Biometrics.Face.OpenCv;
using Testcontainers.Azurite;

namespace SCI_PH.IntegrationTests.Biometrics.Face
{
    // One disposable Azurite (Blob only) per test run. Without Docker the tests are skipped locally; CI sets
    // SCIPH_REQUIRE_DOCKER=true so they must run.
    public sealed class AzuriteFixture : IAsyncLifetime
    {
        public const string Image = "mcr.microsoft.com/azure-storage/azurite:3.37.0";
        private AzuriteContainer? _container;

        public bool Available { get; private set; }

        public string SkipReason { get; private set; } = "Docker is not available.";

        public string ConnectionString => _container!.GetConnectionString();

        public async Task InitializeAsync()
        {
            try
            {
                _container = new AzuriteBuilder(Image).WithInMemoryPersistence().Build();
                await _container.StartAsync();
                Available = true;
            }
            catch (Exception exception) when (!string.Equals(Environment.GetEnvironmentVariable("SCIPH_REQUIRE_DOCKER"), "true", StringComparison.OrdinalIgnoreCase))
            {
                SkipReason = $"Docker is not available ({exception.GetType().Name}); Azurite tests skipped.";
            }
        }

        public async Task DisposeAsync()
        {
            if (_container is not null)
            {
                await _container.DisposeAsync();
            }
        }
    }

    // The encrypted template store on Azure Blob (Azurite): private container, opaque names, owner/format metadata only,
    // never overwritten.
    public class FaceTemplateStoreTests : IClassFixture<AzuriteFixture>
    {
        private static readonly CancellationToken None = CancellationToken.None;
        private readonly AzuriteFixture _azurite;

        public FaceTemplateStoreTests(AzuriteFixture azurite) => _azurite = azurite;

        private BlobContainerClient Container() => new(_azurite.ConnectionString, $"face-templates-{Guid.NewGuid():N}"[..40]);

        [SkippableFact]
        public async Task TemplatesArePrivateOpaqueAndNeverOverwritten()
        {
            Skip.IfNot(_azurite.Available, _azurite.SkipReason);
            var container = Container();
            var store = new BlobFaceTemplateStore(container);
            var id = Guid.NewGuid();

            Assert.Null(await store.GetAsync(id, None));
            Assert.False(await store.DeleteAsync(id, None));
            Assert.Empty(await ListAsync(store));

            await store.PutAsync(id, [1, 2, 3], None);
            Assert.Equal([1, 2, 3], await store.GetAsync(id, None));
            var failure = await Assert.ThrowsAsync<RequestFailedException>(() => store.PutAsync(id, [9], None));
            Assert.Equal(409, failure.Status);
            Assert.Equal([1, 2, 3], await store.GetAsync(id, None));

            var properties = (await container.GetPropertiesAsync()).Value;
            Assert.Equal(PublicAccessType.None, properties.PublicAccess);
            var blobs = new List<BlobItem>();
            await foreach (var item in container.GetBlobsAsync(new GetBlobsOptions { Traits = BlobTraits.Metadata }))
            {
                blobs.Add(item);
            }

            var blob = Assert.Single(blobs);
            Assert.Equal(id.ToString("D"), blob.Name);
            Assert.Equal(new Dictionary<string, string> { ["owner"] = "sciph-face-opencv", ["format"] = "1" }, blob.Metadata);

            // A blob that is not one of ours is never listed as a template.
            await container.GetBlobClient("notes.txt").UploadAsync(BinaryData.FromString("x"));
            await container.GetBlobClient(id.ToString("N")).UploadAsync(BinaryData.FromString("x"));
            Assert.Equal([id], await ListAsync(store));

            Assert.True(await store.DeleteAsync(id, None));
            Assert.False(await store.DeleteAsync(id, None));
            Assert.Null(await store.GetAsync(id, None));
        }

        // Unreachable store: an exception (the provider's 503), never "not found".
        [Fact]
        public async Task AnUnreachableStoreFailsInsteadOfAnsweringNotFound()
        {
            var unreachable = new BlobContainerClient(
                new Uri("http://127.0.0.1:9/devstoreaccount1/face-templates"),
                new BlobClientOptions { Retry = { MaxRetries = 0, NetworkTimeout = TimeSpan.FromSeconds(2) } });
            var store = new BlobFaceTemplateStore(unreachable);

            await Assert.ThrowsAnyAsync<Exception>(() => store.GetAsync(Guid.NewGuid(), None));
            await Assert.ThrowsAnyAsync<Exception>(() => store.DeleteAsync(Guid.NewGuid(), None));
        }

        private static async Task<List<Guid>> ListAsync(IFaceTemplateStore store)
        {
            var ids = new List<Guid>();
            await foreach (var id in store.ListAsync(None))
            {
                ids.Add(id);
            }

            return ids;
        }
    }
}
