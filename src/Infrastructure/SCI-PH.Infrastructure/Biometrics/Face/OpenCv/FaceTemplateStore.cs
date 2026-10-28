using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace SCI_PH.Infrastructure.Biometrics.Face.OpenCv
{
    // Where the local face provider keeps its ENCRYPTED templates (ADR-012, PR 2). Infrastructure only: Application never sees
    // it. Keyed by the opaque template GUID of the subject reference. Any store failure is an exception (the provider turns it
    // into a 503); a missing template is null / false, never an empty template.
    internal interface IFaceTemplateStore
    {
        Task PutAsync(Guid templateId, byte[] envelope, CancellationToken cancellationToken);

        Task<byte[]?> GetAsync(Guid templateId, CancellationToken cancellationToken);

        // False when there was nothing to delete.
        Task<bool> DeleteAsync(Guid templateId, CancellationToken cancellationToken);

        // Every template id in the store, for the reconciliation report only (never deletes).
        IAsyncEnumerable<Guid> ListAsync(CancellationToken cancellationToken);
    }

    // Azure Blob Storage (Azurite in development): one private container, one blob per template named by its GUID, with no
    // metadata but the owner and the format. No patient, reference or key id in names or metadata.
    internal sealed class BlobFaceTemplateStore : IFaceTemplateStore
    {
        public const string OwnerMetadata = "sciph-face-opencv";

        private static readonly Dictionary<string, string> Metadata = new()
        {
            ["owner"] = OwnerMetadata,
            ["format"] = FaceTemplateCipher.EnvelopeVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        private readonly BlobContainerClient _container;
        private readonly SemaphoreSlim _ensure = new(1, 1);
        private volatile bool _ensured;

        public BlobFaceTemplateStore(BlobContainerClient container) => _container = container;

        public async Task PutAsync(Guid templateId, byte[] envelope, CancellationToken cancellationToken)
        {
            await EnsureContainerAsync(cancellationToken);
            // Never overwrites: a template id is used once (If-None-Match: *).
            await Blob(templateId).UploadAsync(
                new BinaryData(envelope),
                new BlobUploadOptions
                {
                    Metadata = Metadata,
                    HttpHeaders = new BlobHttpHeaders { ContentType = "application/octet-stream" },
                    Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
                },
                cancellationToken);
        }

        public async Task<byte[]?> GetAsync(Guid templateId, CancellationToken cancellationToken)
        {
            try
            {
                var result = await Blob(templateId).DownloadContentAsync(cancellationToken);
                return result.Value.Content.ToArray();
            }
            catch (RequestFailedException exception) when (exception.Status == 404)
            {
                return null;
            }
        }

        public async Task<bool> DeleteAsync(Guid templateId, CancellationToken cancellationToken)
        {
            try
            {
                return (await Blob(templateId).DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken)).Value;
            }
            catch (RequestFailedException exception) when (exception.ErrorCode == BlobErrorCode.ContainerNotFound)
            {
                return false;
            }
        }

        public async IAsyncEnumerable<Guid> ListAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (!(await _container.ExistsAsync(cancellationToken)).Value)
            {
                yield break;
            }

            await foreach (var item in _container.GetBlobsAsync(cancellationToken: cancellationToken))
            {
                // Anything that is not one of our names is not ours to report as a template.
                if (Guid.TryParseExact(item.Name, "D", out var id) && string.Equals(item.Name, id.ToString("D"), StringComparison.Ordinal))
                {
                    yield return id;
                }
            }
        }

        private BlobClient Blob(Guid templateId) => _container.GetBlobClient(templateId.ToString("D"));

        // Private container (no public access), created on the first write.
        private async Task EnsureContainerAsync(CancellationToken cancellationToken)
        {
            if (_ensured)
            {
                return;
            }

            await _ensure.WaitAsync(cancellationToken);
            try
            {
                if (!_ensured)
                {
                    await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
                    _ensured = true;
                }
            }
            finally
            {
                _ensure.Release();
            }
        }
    }
}
