using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;
using SCI_PH.Application.Abstractions.Storage;

namespace SCI_PH.Infrastructure.Storage
{
    // DocumentStorage:Provider chooses where document contents live:
    // - "AzureBlob": an Azure Blob container (ConnectionString + Container). Also works against Azurite for local runs.
    // - "FileSystem": a local folder (RootPath), for development and demos only; refused in Production.
    // - empty or "Disabled" (default): no storage; uploads and downloads answer a generic 503.
    public sealed class DocumentStorageOptions
    {
        public const string SectionName = "DocumentStorage";
        public const string AzureBlobProvider = "AzureBlob";
        public const string FileSystemProvider = "FileSystem";
        public const string DisabledProvider = "Disabled";

        public string? Provider { get; set; }
        public string? ConnectionString { get; set; }
        public string Container { get; set; } = "clinical-documents";
        public string? RootPath { get; set; }

        public string ResolvedProvider => string.IsNullOrWhiteSpace(Provider) ? DisabledProvider : Provider.Trim();
    }

    internal sealed class DocumentStorageOptionsValidator(Microsoft.Extensions.Hosting.IHostEnvironment environment)
        : IValidateOptions<DocumentStorageOptions>
    {
        public ValidateOptionsResult Validate(string? name, DocumentStorageOptions options) => options.ResolvedProvider switch
        {
            DocumentStorageOptions.DisabledProvider => ValidateOptionsResult.Success,
            DocumentStorageOptions.AzureBlobProvider when string.IsNullOrWhiteSpace(options.ConnectionString) || string.IsNullOrWhiteSpace(options.Container) =>
                ValidateOptionsResult.Fail("DocumentStorage:AzureBlob requires ConnectionString and Container."),
            DocumentStorageOptions.AzureBlobProvider => ValidateOptionsResult.Success,
            DocumentStorageOptions.FileSystemProvider when environment.EnvironmentName == "Production" =>
                ValidateOptionsResult.Fail("DocumentStorage:FileSystem is for development only and cannot run in Production."),
            DocumentStorageOptions.FileSystemProvider when string.IsNullOrWhiteSpace(options.RootPath) =>
                ValidateOptionsResult.Fail("DocumentStorage:FileSystem requires RootPath."),
            DocumentStorageOptions.FileSystemProvider => ValidateOptionsResult.Success,
            var other => ValidateOptionsResult.Fail($"DocumentStorage:Provider '{other}' is not supported.")
        };
    }

    // Paths come from the Document Service (ids only); this re-checks them so nothing else can escape the container/folder.
    internal static class StoragePath
    {
        public static string Checked(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Length > 512 || path.StartsWith('/') || path.Contains("..", StringComparison.Ordinal)
                || !path.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '/' or '-'))
            {
                throw new ArgumentException("Invalid storage path.", nameof(path));
            }

            return path;
        }
    }

    internal sealed class BlobDocumentStorage(BlobContainerClient container) : IStorageService
    {
        private readonly SemaphoreSlim _created = new(1, 1);
        private volatile bool _exists;

        public async Task UploadAsync(string path, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken)
        {
            await EnsureContainerAsync(cancellationToken);
            // IfNoneMatch "*": never overwrite an existing object.
            await container.GetBlobClient(StoragePath.Checked(path)).UploadAsync(BinaryData.FromBytes(content), new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
                Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
            }, cancellationToken);
        }

        public async Task<Stream?> OpenReadAsync(string path, CancellationToken cancellationToken)
        {
            try
            {
                var download = await container.GetBlobClient(StoragePath.Checked(path)).DownloadStreamingAsync(cancellationToken: cancellationToken);
                return download.Value.Content;
            }
            catch (RequestFailedException exception) when (exception.Status == 404)
            {
                return null;
            }
        }

        public async Task DeleteAsync(string path, CancellationToken cancellationToken) =>
            await container.GetBlobClient(StoragePath.Checked(path)).DeleteIfExistsAsync(cancellationToken: cancellationToken);

        private async Task EnsureContainerAsync(CancellationToken cancellationToken)
        {
            if (_exists)
            {
                return;
            }

            await _created.WaitAsync(cancellationToken);
            try
            {
                if (!_exists)
                {
                    await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
                    _exists = true;
                }
            }
            finally
            {
                _created.Release();
            }
        }
    }

    internal sealed class FileSystemDocumentStorage(string rootPath) : IStorageService
    {
        private readonly string _root = Path.GetFullPath(rootPath);

        public async Task UploadAsync(string path, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken)
        {
            var file = Resolve(path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            // CreateNew: never overwrite an existing object.
            await using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await stream.WriteAsync(content, cancellationToken);
        }

        public Task<Stream?> OpenReadAsync(string path, CancellationToken cancellationToken)
        {
            var file = Resolve(path);
            return Task.FromResult<Stream?>(File.Exists(file) ? new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read) : null);
        }

        public Task DeleteAsync(string path, CancellationToken cancellationToken)
        {
            var file = Resolve(path);
            if (File.Exists(file))
            {
                File.Delete(file);
            }

            return Task.CompletedTask;
        }

        private string Resolve(string path)
        {
            var file = Path.GetFullPath(Path.Combine(_root, StoragePath.Checked(path).Replace('/', Path.DirectorySeparatorChar)));
            return file.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                ? file
                : throw new ArgumentException("Invalid storage path.", nameof(path));
        }
    }

    // No storage configured: the Document Service answers a generic 503; metadata is never written without content.
    internal sealed class DisabledDocumentStorage : IStorageService
    {
        public Task UploadAsync(string path, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Document storage is not configured.");

        public Task<Stream?> OpenReadAsync(string path, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Document storage is not configured.");

        public Task DeleteAsync(string path, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
