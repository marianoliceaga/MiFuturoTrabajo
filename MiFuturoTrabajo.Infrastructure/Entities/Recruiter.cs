namespace MiFuturoTrabajo.Infrastructure.Entities
{
    /// <summary>Aggregate root, container "Recruiters", partition key /UserId.</summary>
    public class Recruiter
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string UserId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Lastname { get; set; } = string.Empty;
        public string ContactEmail { get; set; } = string.Empty;
        public string Cellphone { get; set; } = string.Empty;
        public string Landline { get; set; } = string.Empty;
        public string LinkedInUrl { get; set; } = string.Empty;
        public bool IsFromConsultancy { get; set; }
        public string CompanyName { get; set; } = string.Empty;
    }
}
