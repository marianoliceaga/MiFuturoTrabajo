using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

namespace MiFuturoTrabajo.Api.Services
{
    /// <summary>
    /// Azure Blob Storage over a single private container.
    ///
    /// Two authentication modes, chosen by configuration:
    ///
    ///   AzureStorage:AccountUrl        -> DefaultAzureCredential (managed identity).
    ///                                     Read links are user-delegation SAS, signed
    ///                                     with a key Azure AD issues on demand. No
    ///                                     account key exists anywhere in configuration.
    ///
    ///   AzureStorage:ConnectionString  -> shared key. Read links are service SAS.
    ///                                     This is the local/Azurite path.
    ///
    /// AccountUrl wins when both are set, so a deployed environment cannot
    /// accidentally fall back to a key that lingers in configuration.
    /// </summary>
    public class BlobStorageService : IBlobStorageService
    {
        private const string ContainerName = "cv-uploads";

        private readonly BlobServiceClient _service;
        private readonly BlobContainerClient _container;
        private readonly bool _usesManagedIdentity;
        private readonly ILogger<BlobStorageService> _logger;
        private readonly SemaphoreSlim _containerGate = new(1, 1);
        private bool _containerChecked;

        public BlobStorageService(IConfiguration configuration, ILogger<BlobStorageService> logger)
        {
            _logger = logger;

            var accountUrl = configuration["AzureStorage:AccountUrl"];
            var connectionString = configuration["AzureStorage:ConnectionString"];

            if (!string.IsNullOrWhiteSpace(accountUrl))
            {
                _usesManagedIdentity = true;
                _service = new BlobServiceClient(new Uri(accountUrl), new DefaultAzureCredential());
                _container = _service.GetBlobContainerClient(ContainerName);
            }
            else if (!string.IsNullOrWhiteSpace(connectionString))
            {
                _usesManagedIdentity = false;
                _service = new BlobServiceClient(connectionString);
                _container = _service.GetBlobContainerClient(ContainerName);
            }
            else
            {
                throw new InvalidOperationException(
                    "Blob storage is not configured. Set AzureStorage:AccountUrl (managed identity, " +
                    "preferred) or AzureStorage:ConnectionString (local Azurite).");
            }
        }

        /// <summary>
        /// The container is created on first use rather than per operation, and
        /// always private - a public container would make every CV in the system
        /// readable by anyone who could guess a path.
        /// </summary>
        private async Task EnsureContainerAsync(CancellationToken cancellationToken)
        {
            if (_containerChecked) return;

            await _containerGate.WaitAsync(cancellationToken);
            try
            {
                if (_containerChecked) return;
                await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
                _containerChecked = true;
            }
            finally
            {
                _containerGate.Release();
            }
        }

        public async Task UploadAsync(Stream content, string blobPath, string contentType, CancellationToken cancellationToken = default)
        {
            await EnsureContainerAsync(cancellationToken);

            var blob = _container.GetBlobClient(blobPath);

            await blob.UploadAsync(content, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
            }, cancellationToken);
        }

        public async Task<BlobReadLink> GetReadLinkAsync(
            string blobPath, string downloadFileName, TimeSpan lifetime, CancellationToken cancellationToken = default)
        {
            var blob = _container.GetBlobClient(blobPath);

            // Backdated slightly so a few seconds of clock skew between this
            // host and the storage service cannot produce a link that is not
            // valid yet.
            var startsOn = DateTimeOffset.UtcNow.AddMinutes(-5);
            var expiresOn = DateTimeOffset.UtcNow.Add(lifetime);

            var builder = new BlobSasBuilder
            {
                BlobContainerName = _container.Name,
                BlobName = blobPath,
                Resource = "b",
                StartsOn = startsOn,
                ExpiresOn = expiresOn,
                ContentDisposition = $"attachment; filename=\"{SanitiseForHeader(downloadFileName)}\""
            };
            builder.SetPermissions(BlobSasPermissions.Read);

            Uri url;

            if (_usesManagedIdentity)
            {
                // A user-delegation key is signed by Azure AD and inherits the
                // identity's permissions, so revoking the role assignment
                // revokes every link already handed out.
                var delegationKey = await _service.GetUserDelegationKeyAsync(startsOn, expiresOn, cancellationToken);

                var query = builder.ToSasQueryParameters(delegationKey.Value, _service.AccountName);
                url = new UriBuilder(blob.Uri) { Query = query.ToString() }.Uri;
            }
            else
            {
                if (!blob.CanGenerateSasUri)
                {
                    throw new InvalidOperationException(
                        "Cannot mint a download link: the blob client has no shared key and no " +
                        "managed identity is configured.");
                }

                url = blob.GenerateSasUri(builder);
            }

            return new BlobReadLink(url, expiresOn.UtcDateTime);
        }

        public async Task DeleteAsync(string blobPath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(blobPath)) return;

            try
            {
                await _container.GetBlobClient(blobPath).DeleteIfExistsAsync(cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                // The database row is already gone by this point. Losing the blob
                // leaves an orphan that costs a fraction of a cent; failing the
                // request would leave the user with a CV they cannot remove.
                _logger.LogError(ex, "Failed to delete blob {BlobPath}; it is now orphaned", blobPath);
            }
        }

        /// <summary>
        /// Quotes and control characters in a filename would break the
        /// Content-Disposition header, so they do not reach it.
        /// </summary>
        private static string SanitiseForHeader(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return "cv";

            var cleaned = new string(fileName
                .Where(c => !char.IsControl(c) && c != '"' && c != '\\')
                .ToArray());

            return string.IsNullOrWhiteSpace(cleaned) ? "cv" : cleaned;
        }
    }
}
