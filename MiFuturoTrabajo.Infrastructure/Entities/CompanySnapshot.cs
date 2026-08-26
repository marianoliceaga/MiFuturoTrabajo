namespace MiFuturoTrabajo.Infrastructure.Entities
{
    /// <summary>
    /// Deliberate denormalisation, embedded in the JobApplication document.
    /// Cosmos cannot join across containers and the board needs the company
    /// name on every card, so the name is copied in at write time. Renaming a
    /// company fans the new name out across that user's applications.
    /// </summary>
    public class CompanySnapshot
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }
}
