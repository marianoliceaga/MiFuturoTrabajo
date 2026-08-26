namespace MiFuturoTrabajo.Infrastructure.Entities
{
    public class CVVersion
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ApplicantId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string FileUrl { get; set; } = string.Empty;
        public DateTime UploadedDate { get; set; } = DateTime.UtcNow;
        public bool IsDefault { get; set; }
        public string VersionNotes { get; set; } = string.Empty;

        // Navigation property
        public Applicant Applicant { get; set; } = null!;
    }
}
