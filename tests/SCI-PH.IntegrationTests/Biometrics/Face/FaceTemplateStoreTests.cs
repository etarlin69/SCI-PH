using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SCI_PH.Application.Abstractions.Biometrics;
using SCI_PH.Application.Features.Clinical;
using SCI_PH.Domain.Enums.Patients;
using SCI_PH.Infrastructure.Biometrics.Face.OpenCv;
using SCI_PH.IntegrationTests.Security.Support;
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
    // never overwritten; and the report-only reconciliation of the store with the clinical references.
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

        // Reconciliation A (an active reference without its template), B (a template no retained reference points to) and the
        // cleanup jobs needing attention are reported with opaque ids only, and nothing is deleted.
        [Fact]
        public async Task ReconciliationReportsMissingAndOrphanTemplatesWithoutDeleting()
        {
            var store = new OpenCvFaceProviderTests.InMemoryTemplateStore();
            var (active, missing, pendingCleanup, orphan) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            foreach (var id in new[] { active, pendingCleanup, orphan })
            {
                store.Blobs[id] = [1];
            }

            var now = DateTimeOffset.UtcNow;
            var struggling = FaceTemplateDeletion.Request(Guid.NewGuid(), now) with { Attempts = FaceTemplateCleanupService.AttentionAttempts };
            var retained = new RetainedStore(
            [
                new RetainedFaceTemplate(FaceSubjectReference.Format(active), Active: true),
                new RetainedFaceTemplate(FaceSubjectReference.Format(missing), Active: true),
                new RetainedFaceTemplate(FaceSubjectReference.Format(pendingCleanup), Active: false)
            ],
            [
                new FaceTemplateDeletionTarget(FaceTemplateDeletion.Request(Guid.NewGuid(), now) with { Attempts = 4 },
                    Guid.NewGuid(), BiometricModality.Face, "SCIPH-FaceOpenCV", FaceSubjectReference.Format(pendingCleanup)),
                new FaceTemplateDeletionTarget(struggling, Guid.NewGuid(), BiometricModality.Face, "SCIPH-FaceOpenCV", "face:x")
            ]);
            var services = new ServiceCollection().AddScoped<IFaceTemplateDeletionStore>(_ => retained).BuildServiceProvider();
            var logs = new CapturingLoggerProvider();
            var reconciler = new FaceTemplateReconciler(
                services.GetRequiredService<IServiceScopeFactory>(), store,
                LoggerFactory.Create(b => b.AddProvider(logs)).CreateLogger<FaceTemplateReconciler>());

            var report = await reconciler.ReportAsync(None);

            Assert.Equal(3, report.Stored);
            Assert.Equal([missing], report.Missing);
            Assert.Equal([orphan], report.Orphans);
            Assert.Equal([struggling.Id], report.Attention);
            Assert.Equal(3, store.Blobs.Count);
            Assert.Equal(LocalOpenCvFaceVerificationService.ProviderName, retained.Provider);
            Assert.Contains($"FaceTemplateMissing: 1 active face reference(s) without a template: {missing}", logs.Text);
            Assert.Contains($"FaceTemplateOrphan: 1 template(s) without a retained reference (not deleted): {orphan}", logs.Text);
            Assert.Contains($"FaceTemplateCleanupAttention: 1 cleanup job(s) failed 5 or more times and keep being retried: {struggling.Id}", logs.Text);
            Assert.DoesNotContain("face:", logs.Text);
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

        private sealed class RetainedStore(IReadOnlyList<RetainedFaceTemplate> retained, IReadOnlyList<FaceTemplateDeletionTarget> pending)
            : IFaceTemplateDeletionStore
        {
            public string? Provider { get; private set; }

            public Task<IReadOnlyList<RetainedFaceTemplate>> ListRetainedAsync(string provider, CancellationToken cancellationToken)
            {
                Provider = provider;
                return Task.FromResult(retained);
            }

            public Task<FaceTemplateDeletionTarget?> FindAsync(Guid deletionId, CancellationToken cancellationToken) => throw new NotSupportedException();

            public Task<IReadOnlyList<FaceTemplateDeletionTarget>> ListPendingAsync(int limit, CancellationToken cancellationToken) =>
                Task.FromResult(pending);

            public void Add(FaceTemplateDeletion deletion) => throw new NotSupportedException();

            public void Update(FaceTemplateDeletion deletion, FaceTemplateDeletionStatus expectedStatus, int expectedAttempts) =>
                throw new NotSupportedException();
        }
    }
}
