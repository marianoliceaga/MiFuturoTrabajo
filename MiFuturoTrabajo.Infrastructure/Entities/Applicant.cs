namespace MiFuturoTrabajo.Infrastructure.Entities
{
    /// <summary>
    /// Aggregate root, container "Applicants", partition key /UserId.
    /// CV versions and past job references are embedded: both are small,
    /// bounded, and only ever read together with their applicant.
    /// </summary>
    public class Applicant
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string UserId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Lastname { get; set; } = string.Empty;
        public string CurrentJob { get; set; } = string.Empty;
        public string LinkedInUrl { get; set; } = string.Empty;
        public string ContactEmail { get; set; } = string.Empty;
        public string Cellphone { get; set; } = string.Empty;
        public DateTime CreatedOnDate { get; set; } = DateTime.UtcNow;

        // Embedded (owned) collections - stored inside this document.
        public List<CVVersion> CVVersions { get; set; } = new();
        public List<JobReference> PastJobReferences { get; set; } = new();
    }
}
