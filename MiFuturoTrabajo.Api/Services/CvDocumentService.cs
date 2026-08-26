using MiFuturoTrabajo.Infrastructure.Entities;
using MiFuturoTrabajo.Shared.Models;

namespace MiFuturoTrabajo.Api.Services
{
    public interface ICvDocumentService
    {
        /// <summary>
        /// Validates the upload and, if it passes, stores the bytes and returns a
        /// populated <see cref="CVVersion"/>. The caller adds it to the applicant
        /// and saves; this service does not touch the database.
        /// </summary>
        Task<(CVVersion? Cv, string? Error)> StoreAsync(
            string userId, IFormFile file, string? displayName, CancellationToken cancellationToken = default);

        /// <summary>Null when the CV version has no document attached.</summary>
        Task<CVDownloadLinkDto?> CreateDownloadLinkAsync(CVVersion cv, CancellationToken cancellationToken = default);

        /// <summary>Removes the stored document, if there is one.</summary>
        Task RemoveAsync(CVVersion cv, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The CV document rules in one place, because two controllers expose CV
    /// versions and duplicating "validate, then store, then remember where"
    /// across both is how the two surfaces drift apart.
    /// </summary>
    public class CvDocumentService : ICvDocumentService
    {
        /// <summary>
        /// Long enough for a person to click through to the download, short
        /// enough that a link copied out of browser history is dead by the time
        /// anyone else finds it.
        /// </summary>
        private static readonly TimeSpan LinkLifetime = TimeSpan.FromMinutes(15);

        private readonly IBlobStorageService _blobs;
        private readonly ILogger<CvDocumentService> _logger;

        public CvDocumentService(IBlobStorageService blobs, ILogger<CvDocumentService> logger)
        {
            _blobs = blobs;
            _logger = logger;
        }

        public async Task<(CVVersion? Cv, string? Error)> StoreAsync(
            string userId, IFormFile file, string? displayName, CancellationToken cancellationToken = default)
        {
            if (file is null || file.Length == 0)
            {
                return (null, "No file was uploaded.");
            }

            var header = await ReadHeaderAsync(file, cancellationToken);
            var validation = CvFileValidator.Validate(file.FileName, file.Length, header);

            if (!validation.IsValid)
            {
                return (null, validation.Error);
            }

            var cvId = Guid.NewGuid().ToString();

            // Partitioning by user id means one person's documents are a prefix
            // in the container, which keeps listing and cleanup per-user, and
            // makes a mistakenly broad SAS scope visible rather than silent.
            var blobPath = $"{Sanitise(userId)}/{cvId}{validation.Extension}";

            using (var content = file.OpenReadStream())
            {
                await _blobs.UploadAsync(content, blobPath, validation.ContentType, cancellationToken);
            }

            var fileName = Path.GetFileName(file.FileName);

            _logger.LogInformation(
                "Stored CV {CvId} for user {UserId} ({Bytes} bytes, {ContentType})",
                cvId, userId, file.Length, validation.ContentType);

            return (new CVVersion
            {
                Id = cvId,
                Name = string.IsNullOrWhiteSpace(displayName)
                    ? Path.GetFileNameWithoutExtension(fileName)
                    : displayName.Trim(),
                UploadedDate = DateTime.UtcNow,
                BlobPath = blobPath,
                FileName = fileName,
                ContentType = validation.ContentType,
                SizeBytes = file.Length
            }, null);
        }

        public async Task<CVDownloadLinkDto?> CreateDownloadLinkAsync(
            CVVersion cv, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(cv.BlobPath))
            {
                return null;
            }

            var fileName = string.IsNullOrWhiteSpace(cv.FileName) ? $"{cv.Name}.pdf" : cv.FileName;
            var link = await _blobs.GetReadLinkAsync(cv.BlobPath, fileName, LinkLifetime, cancellationToken);

            return new CVDownloadLinkDto
            {
                Url = link.Url.ToString(),
                FileName = fileName,
                ExpiresAtUtc = link.ExpiresAtUtc
            };
        }

        public Task RemoveAsync(CVVersion cv, CancellationToken cancellationToken = default) =>
            string.IsNullOrEmpty(cv.BlobPath)
                ? Task.CompletedTask
                : _blobs.DeleteAsync(cv.BlobPath, cancellationToken);

        private static async Task<byte[]> ReadHeaderAsync(IFormFile file, CancellationToken cancellationToken)
        {
            var buffer = new byte[CvFileValidator.HeaderBytesNeeded];

            using var stream = file.OpenReadStream();

            var read = 0;
            while (read < buffer.Length)
            {
                var got = await stream.ReadAsync(buffer.AsMemory(read, buffer.Length - read), cancellationToken);
                if (got == 0) break;
                read += got;
            }

            return read == buffer.Length ? buffer : buffer[..read];
        }

        /// <summary>
        /// The user id comes from a signed token rather than from the request,
        /// so this is belt-and-braces: it guarantees the id can never climb out
        /// of its own prefix in the container.
        /// </summary>
        private static string Sanitise(string value) =>
            new(value.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray());
    }
}
