using Azure.Storage.Blobs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using SCI_PH.Application.Abstractions.Storage;
using SCI_PH.Application.Features.Documents;
using SCI_PH.Infrastructure.Storage;
using SCI_PH.IntegrationTests.Biometrics.Face;

namespace SCI_PH.IntegrationTests.Documents
{
    // The same Storage Service contract for every provider: write, read back, never overwrite, missing is null, delete, and
    // nothing outside the generated path shape. Azure Blob runs against Azurite (the Azure SDK, not real Azure).
    public sealed class DocumentStorageTests : IClassFixture<AzuriteFixture>, IDisposable
    {
        private static readonly CancellationToken None = CancellationToken.None;
        private static readonly byte[] Content = "%PDF-1.4 prueba"u8.ToArray();
        private readonly AzuriteFixture _azurite;
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"sciph-documents-{Guid.NewGuid():N}");

        public DocumentStorageTests(AzuriteFixture azurite) => _azurite = azurite;

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        private static string NewPath() => $"encounters/{Guid.NewGuid():N}/{Guid.NewGuid():N}";

        private static async Task AssertContractAsync(IStorageService storage)
        {
            var path = NewPath();
            Assert.Null(await storage.OpenReadAsync(path, None));

            await storage.UploadAsync(path, Content, ClinicalDocumentService.Pdf, None);
            await using (var read = await storage.OpenReadAsync(path, None))
            {
                using var copy = new MemoryStream();
                await read!.CopyToAsync(copy);
                Assert.Equal(Content, copy.ToArray());
            }

            // Never overwritten.
            await Assert.ThrowsAnyAsync<Exception>(() => storage.UploadAsync(path, "otro"u8.ToArray(), ClinicalDocumentService.Pdf, None));
            await using (var again = await storage.OpenReadAsync(path, None))
            {
                using var copy = new MemoryStream();
                await again!.CopyToAsync(copy);
                Assert.Equal(Content, copy.ToArray());
            }

            await storage.DeleteAsync(path, None);
            Assert.Null(await storage.OpenReadAsync(path, None));
            await storage.DeleteAsync(path, None); // deleting twice is fine

            foreach (var bad in new[] { "../outside", "/absolute/path", "encounters/../../x", "Encounters/UPPER", "a\\b", "" })
            {
                await Assert.ThrowsAsync<ArgumentException>(() => storage.UploadAsync(bad, Content, ClinicalDocumentService.Pdf, None));
            }
        }

        [SkippableFact]
        public async Task AzureBlobFollowsTheContractWithAPrivateContainer()
        {
            Skip.IfNot(_azurite.Available, _azurite.SkipReason);
            var container = new BlobContainerClient(_azurite.ConnectionString, $"docs-{Guid.NewGuid():N}"[..30]);

            await AssertContractAsync(new BlobDocumentStorage(container));

            var properties = await container.GetPropertiesAsync();
            Assert.Equal(Azure.Storage.Blobs.Models.PublicAccessType.None, properties.Value.PublicAccess);
        }

        [Fact]
        public async Task TheFileSystemFollowsTheContractInsideItsFolder()
        {
            await AssertContractAsync(new FileSystemDocumentStorage(_root));

            Assert.All(Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories),
                file => Assert.StartsWith(Path.GetFullPath(_root), Path.GetFullPath(file)));
        }

        [Fact]
        public async Task WithoutAProviderTheStorageIsUnavailable()
        {
            var storage = new DisabledDocumentStorage();

            await Assert.ThrowsAsync<InvalidOperationException>(() => storage.UploadAsync(NewPath(), Content, ClinicalDocumentService.Pdf, None));
            await Assert.ThrowsAsync<InvalidOperationException>(() => storage.OpenReadAsync(NewPath(), None));
        }

        [Theory]
        [InlineData(null, null, null, "Production", true)]
        [InlineData("Disabled", null, null, "Production", true)]
        [InlineData("AzureBlob", "UseDevelopmentStorage=true", null, "Production", true)]
        [InlineData("AzureBlob", null, null, "Production", false)]
        [InlineData("FileSystem", null, "C:/sciph-docs", "Development", true)]
        [InlineData("FileSystem", null, null, "Development", false)]
        [InlineData("FileSystem", null, "C:/sciph-docs", "Production", false)]
        [InlineData("S3", null, null, "Development", false)]
        public void TheConfigurationIsValidatedAtStartup(string? provider, string? connectionString, string? rootPath, string environment, bool valid)
        {
            var validator = new DocumentStorageOptionsValidator(new HostingEnvironment { EnvironmentName = environment });

            var result = validator.Validate(null, new DocumentStorageOptions { Provider = provider, ConnectionString = connectionString, RootPath = rootPath });

            Assert.Equal(valid, result.Succeeded);
        }

        [Theory]
        [InlineData("%PDF-1.7", ClinicalDocumentService.Pdf)]
        [InlineData("\u0089PNG\r\n\u001a\n", ClinicalDocumentService.Png)]
        public void TheTypeComesFromTheSignature(string start, string expected)
        {
            var bytes = start.Select(c => (byte)c).ToArray();

            Assert.Equal(expected, ClinicalDocumentService.DetectContentType(bytes));
        }

        [Fact]
        public void OtherContentIsRejected()
        {
            Assert.Equal(ClinicalDocumentService.Jpeg, ClinicalDocumentService.DetectContentType([0xFF, 0xD8, 0xFF, 0xE0]));
            Assert.Null(ClinicalDocumentService.DetectContentType([0x4D, 0x5A])); // MZ executable
            Assert.Null(ClinicalDocumentService.DetectContentType("<html>"u8));
            Assert.Null(ClinicalDocumentService.DetectContentType([]));
        }

        [Theory]
        [InlineData("../../etc/passwd", ClinicalDocumentService.Pdf, "passwd.pdf")]
        [InlineData("C:\\Users\\x\\informe final.PDF", ClinicalDocumentService.Pdf, "informe final.pdf")]
        [InlineData("foto.exe", ClinicalDocumentService.Png, "foto.png")]
        [InlineData("   ", ClinicalDocumentService.Jpeg, "documento.jpg")]
        [InlineData(null, ClinicalDocumentService.Pdf, "documento.pdf")]
        [InlineData("a<b>:c|d?e*\u0001.pdf", ClinicalDocumentService.Pdf, "abcde.pdf")]
        public void TheFileNameIsOnlyASafeDisplayName(string? name, string contentType, string expected)
        {
            Assert.Equal(expected, ClinicalDocumentService.SanitizeFileName(name, contentType));
        }

        [Fact]
        public void ALongNameIsBoundedAndKeepsItsExtension()
        {
            var name = ClinicalDocumentService.SanitizeFileName(new string('x', 400) + ".pdf", ClinicalDocumentService.Pdf);

            Assert.Equal(255, name.Length);
            Assert.EndsWith(".pdf", name);
        }
    }
}
