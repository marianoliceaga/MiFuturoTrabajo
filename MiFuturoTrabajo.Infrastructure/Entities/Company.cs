namespace MiFuturoTrabajo.Infrastructure.Entities
{
    /// <summary>Aggregate root, container "Companies", partition key /UserId.</summary>
    public class Company
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string UserId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Website { get; set; } = string.Empty;
        public string Telephone { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public List<string> Labels { get; set; } = new();
    }
}
