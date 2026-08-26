namespace MiFuturoTrabajo.Infrastructure.Entities
{
    /// <summary>Owned type, embedded in the Applicant document.</summary>
    public class JobReference
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string CompanyName { get; set; } = string.Empty;
        public string JobTitle { get; set; } = string.Empty;
        public string ContactName { get; set; } = string.Empty;
        public string ContactEmail { get; set; } = string.Empty;
        public string ContactPhone { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
    }
}
