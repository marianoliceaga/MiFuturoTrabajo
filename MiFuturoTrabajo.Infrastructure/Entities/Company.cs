namespace MiFuturoTrabajo.Infrastructure.Entities
{
    public class Company
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string UserId { get; set; } = string.Empty; // User isolation
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Website { get; set; } = string.Empty;
        public string Telephone { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public string LabelsJson { get; set; } = "[]"; // Store as JSON string

        // Navigation properties
        public ICollection<JobApplication> JobApplications { get; set; } = new List<JobApplication>();
    }
}
