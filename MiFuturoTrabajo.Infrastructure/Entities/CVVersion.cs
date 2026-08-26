namespace MiFuturoTrabajo.Infrastructure.Entities
{
    /// <summary>Owned type, embedded in the Applicant document.</summary>
    public class CVVersion
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public DateTime UploadedDate { get; set; } = DateTime.UtcNow;
        public bool IsDefault { get; set; }
        public string VersionNotes { get; set; } = string.Empty;

        /// <summary>
        /// Key of the blob in the private container - not a URL. The container
        /// is private, so a URL stored here would not be openable anyway; read
        /// links are minted on demand and expire. Empty when the user recorded
        /// a CV version without attaching a file.
        /// </summary>
        public string BlobPath { get; set; } = string.Empty;

        /// <summary>Original filename, so a download arrives named as the user expects.</summary>
        public string FileName { get; set; } = string.Empty;

        public string ContentType { get; set; } = string.Empty;

        public long SizeBytes { get; set; }
    }
}
