using Microsoft.EntityFrameworkCore;
using MiFuturoTrabajo.Infrastructure;

namespace MiFuturoTrabajo.Api.Services
{
    /// <summary>
    /// Per-user caps, configurable under <c>Quotas:</c>.
    /// </summary>
    public class QuotaOptions
    {
        public int JobApplications { get; set; } = 200;
        public int Companies { get; set; } = 100;
        public int Recruiters { get; set; } = 100;
        public int Meetings { get; set; } = 300;
        public int Tasks { get; set; } = 300;
        public int CVVersions { get; set; } = 10;
    }

    /// <summary>
    /// How much one account may store.
    ///
    /// Registration is open on a free-tier database, so without a ceiling one
    /// script can fill 25 GB of Cosmos, exhaust the 1000 RU/s everyone shares,
    /// and take the demo down for everyone - including the reviewer it exists
    /// for. Rate limiting caps how fast; this caps how much.
    ///
    /// The limits are generous by design. A real job hunt is dozens of
    /// applications, not hundreds, so nobody using the app for its purpose will
    /// meet one.
    /// </summary>
    public class QuotaService
    {
        private readonly ApplicationDbContext _context;
        private readonly QuotaOptions _quotas;

        public QuotaService(ApplicationDbContext context, QuotaOptions quotas)
        {
            _context = context;
            _quotas = quotas;
        }

        public QuotaOptions Limits => _quotas;

        /// <summary>
        /// Null when there is room, or a message naming the limit when there is
        /// not. Callers turn that into a 429 - a refusal the caller can act on,
        /// rather than a 500 when the store eventually complains.
        /// </summary>
        public async Task<string?> CheckJobApplicationsAsync(string applicantId, CancellationToken ct = default) =>
            await CheckAsync(
                _context.JobApplications.Where(x => x.ApplicantId == applicantId && !x.IsDeleted).CountAsync(ct),
                _quotas.JobApplications, "job applications");

        public async Task<string?> CheckCompaniesAsync(string userId, CancellationToken ct = default) =>
            await CheckAsync(
                _context.Companies.Where(x => x.UserId == userId).CountAsync(ct),
                _quotas.Companies, "companies");

        public async Task<string?> CheckRecruitersAsync(string userId, CancellationToken ct = default) =>
            await CheckAsync(
                _context.Recruiters.Where(x => x.UserId == userId).CountAsync(ct),
                _quotas.Recruiters, "recruiters");

        public async Task<string?> CheckMeetingsAsync(string applicantId, CancellationToken ct = default) =>
            await CheckAsync(
                _context.Meetings.Where(x => x.ApplicantId == applicantId).CountAsync(ct),
                _quotas.Meetings, "meetings");

        public async Task<string?> CheckTasksAsync(string applicantId, CancellationToken ct = default) =>
            await CheckAsync(
                _context.ApplicantTasks.Where(x => x.ApplicantId == applicantId).CountAsync(ct),
                _quotas.Tasks, "tasks");

        /// <summary>
        /// CV versions are embedded in the applicant document, so the count is
        /// already in memory - no query, and no cost.
        /// </summary>
        public string? CheckCVVersions(int currentCount) =>
            currentCount >= _quotas.CVVersions
                ? Message(_quotas.CVVersions, "CV versions")
                : null;

        private static async Task<string?> CheckAsync(Task<int> countTask, int limit, string noun)
        {
            var current = await countTask;

            return current >= limit ? Message(limit, noun) : null;
        }

        private static string Message(int limit, string noun) =>
            $"You have reached the limit of {limit} {noun} for this account. " +
            "Delete something you no longer need, or get in touch if you need more room.";
    }
}
