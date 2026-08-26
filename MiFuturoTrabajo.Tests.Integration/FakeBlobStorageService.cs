using System.Collections.Concurrent;
using MiFuturoTrabajo.Api.Services;

namespace MiFuturoTrabajo.Tests.Integration
{
    /// <summary>
    /// In-memory stand-in for blob storage, so the integration suite needs no
    /// Azurite process and no storage account - the same reason the suite ships
    /// its own appsettings.Testing.json.
    ///
    /// It is registered as a singleton and exposes its contents, so a test can
    /// assert on what is actually in storage rather than only on what the API
    /// said. That is what makes "delete really deletes the blob" testable.
    /// </summary>
    public class FakeBlobStorageService : IBlobStorageService
    {
        private readonly ConcurrentDictionary<string, byte[]> _blobs = new();

        public IReadOnlyCollection<string> StoredPaths => _blobs.Keys.ToList();

        public int Count => _blobs.Count;

        public bool Exists(string blobPath) => _blobs.ContainsKey(blobPath);

        public byte[]? Read(string blobPath) => _blobs.TryGetValue(blobPath, out var bytes) ? bytes : null;

        public void Clear() => _blobs.Clear();

        public async Task UploadAsync(Stream content, string blobPath, string contentType, CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            _blobs[blobPath] = buffer.ToArray();
        }

        public Task<BlobReadLink> GetReadLinkAsync(string blobPath, string downloadFileName, TimeSpan lifetime, CancellationToken cancellationToken = default)
        {
            if (!_blobs.ContainsKey(blobPath))
            {
                throw new InvalidOperationException($"No blob at '{blobPath}'.");
            }

            var url = new Uri($"https://fake.blob.test/cv-uploads/{blobPath}?sig=fake&expiry={DateTime.UtcNow.Add(lifetime):O}");

            return Task.FromResult(new BlobReadLink(url, DateTime.UtcNow.Add(lifetime)));
        }

        public Task DeleteAsync(string blobPath, CancellationToken cancellationToken = default)
        {
            _blobs.TryRemove(blobPath, out _);
            return Task.CompletedTask;
        }
    }
}
