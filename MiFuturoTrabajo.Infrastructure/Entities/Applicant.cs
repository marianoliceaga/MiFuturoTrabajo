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

        /// <summary>
        /// AI assistance used in the current period. Kept here rather than in a
        /// container of its own because every request that spends a credit has
        /// already loaded this document to reach the CV, so counting is free -
        /// the same reasoning that puts CV versions inside it.
        ///
        /// A counter that only ever climbs would be simpler, but this is a
        /// metered feature with real per-call cost, and the meter has to reset.
        /// <see cref="AiCreditsPeriodStartUtc"/> is when the current period
        /// began; see <c>AiUsageWindow</c> for the rollover.
        /// </summary>
        public int AiCreditsUsed { get; set; }

        public DateTime AiCreditsPeriodStartUtc { get; set; }

        // Embedded (owned) collections - stored inside this document.
        public List<CVVersion> CVVersions { get; set; } = new();
        public List<JobReference> PastJobReferences { get; set; } = new();
    }
}
