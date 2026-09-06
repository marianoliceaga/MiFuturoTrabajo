namespace MiFuturoTrabajo.Api.Services
{
    /// <summary>
    /// A short-lived, authenticated URL for reading one blob.
    /// </summary>
    /// <param name="Url">The URL to hand to the browser.</param>
    /// <param name="ExpiresAtUtc">When it stops working.</param>
    public record BlobReadLink(Uri Url, DateTime ExpiresAtUtc);

    /// <summary>
    /// Blob storage, addressed by <em>path</em> rather than by URL.
    ///
    /// The distinction matters: the container is private, so a stored URL is
    /// not something a browser can open. Persisting the path and minting a
    /// short-lived read link on demand means a leaked database row is not a
    /// leaked document, and access can be revoked by rotating the key or the
    /// managed identity's role assignment.
    /// </summary>
    public interface IBlobStorageService
    {
        /// <summary>Uploads (or overwrites) the blob at <paramref name="blobPath"/>.</summary>
        Task UploadAsync(Stream content, string blobPath, string contentType, CancellationToken cancellationToken = default);

        /// <summary>
        /// Mints a read-only link valid for <paramref name="lifetime"/>. The link
        /// carries a Content-Disposition so the browser saves the file under
        /// <paramref name="downloadFileName"/> rather than under the blob's GUID name.
        /// </summary>
        Task<BlobReadLink> GetReadLinkAsync(string blobPath, string downloadFileName, TimeSpan lifetime, CancellationToken cancellationToken = default);

        /// <summary>Deletes the blob if it exists. Never throws for a missing blob.</summary>
        Task DeleteAsync(string blobPath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads a blob into memory, or null when it is not there.
        ///
        /// Distinct from <see cref="GetReadLinkAsync"/> because the caller is
        /// different: a link is for handing to a browser, this is for content
        /// the API itself has to look at. Returns bytes rather than a stream
        /// because every caller needs the whole document anyway, and
        /// <paramref name="maxBytes"/> only means something if the size is
        /// checked before the transfer rather than after.
        /// </summary>
        /// <param name="maxBytes">
        /// Refused with null above this size, without downloading. Uploads are
        /// already capped, but the cap has changed before and a blob written
        /// under an older, larger one must not be able to surprise a caller
        /// that budgeted for the current one.
        /// </param>
        Task<byte[]?> DownloadAsync(string blobPath, long maxBytes, CancellationToken cancellationToken = default);
    }
}
