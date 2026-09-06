using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using MiFuturoTrabajo.Api.Helpers;
using MiFuturoTrabajo.Api.Services;
using MiFuturoTrabajo.Infrastructure;
using MiFuturoTrabajo.Infrastructure.Entities;
using MiFuturoTrabajo.Shared.Models;

namespace MiFuturoTrabajo.Api.Controllers
{
    /// <summary>
    /// CV tailoring against a job posting.
    ///
    /// This is the only endpoint in the app where serving a request costs money
    /// per call rather than a fraction of a request unit, so it is metered
    /// twice: a burst limit on the policy, and a per-account allowance held in
    /// the applicant document.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    [EnableRateLimiting(RateLimiting.AiPolicy)]
    public class CvTailoringController : ControllerBase
    {
        private readonly ILogger<CvTailoringController> _logger;
        private readonly ApplicationDbContext _context;
        private readonly ICvTailoringService _tailoring;
        private readonly IBlobStorageService _blobs;

        public CvTailoringController(
            ILogger<CvTailoringController> logger,
            ApplicationDbContext context,
            ICvTailoringService tailoring,
            IBlobStorageService blobs)
        {
            _logger = logger;
            _context = context;
            _tailoring = tailoring;
            _blobs = blobs;
        }

        private Task<Applicant?> LoadApplicantAsync(string userId) =>
            _context.Applicants.Where(a => a.UserId == userId).FirstOrDefaultAsync();

        /// <summary>
        /// What the page needs to render itself honestly before the user types
        /// anything: whether the feature exists on this deployment at all, and
        /// how much of the allowance is left.
        /// </summary>
        [HttpGet("status")]
        public async Task<IActionResult> Status()
        {
            var userId = AuthHelper.GetUserIdFromClaims(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("Unauthorized");
            }

            try
            {
                var applicant = await LoadApplicantAsync(userId);
                if (applicant == null)
                {
                    return NotFound("Applicant not found");
                }

                var options = _tailoring.Options;

                // Read-only: the window is evaluated for display but not
                // persisted here. Writing the document on a GET would turn
                // opening a page into a billable write, and the next POST
                // recomputes it from the same inputs anyway.
                var state = AiUsageWindow.Current(
                    new AiUsageState(applicant.AiCreditsPeriodStartUtc, applicant.AiCreditsUsed),
                    options.Period,
                    DateTime.UtcNow);

                return Ok(new CvTailoringStatusDto
                {
                    Enabled = _tailoring.IsEnabled,
                    Usage = ToDto(state, options),
                    MaxJobDescriptionChars = options.MaxJobDescriptionChars
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading AI status");
                return StatusCode(500, "An error occurred while checking AI availability");
            }
        }

        [HttpPost]
        public async Task<IActionResult> Tailor([FromBody] CvTailoringRequestDto request)
        {
            var userId = AuthHelper.GetUserIdFromClaims(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("Unauthorized");
            }

            if (request == null)
            {
                return BadRequest("Invalid request");
            }

            var options = _tailoring.Options;

            // Everything cheap and local is checked before a credit is spent,
            // so a misconfigured deployment or a malformed request never costs
            // the user part of their allowance.
            if (!_tailoring.IsEnabled)
            {
                return StatusCode(503, "AI assistance is not configured on this deployment.");
            }

            if (string.IsNullOrWhiteSpace(request.JobDescription))
            {
                return BadRequest("Paste the job description first.");
            }

            if (request.JobDescription.Length > options.MaxJobDescriptionChars)
            {
                return BadRequest(
                    $"That job description is {request.JobDescription.Length} characters. " +
                    $"The limit is {options.MaxJobDescriptionChars} - paste the requirements rather than the whole page.");
            }

            if (!Enum.IsDefined(request.Mode))
            {
                return BadRequest("Unknown request mode.");
            }

            try
            {
                var applicant = await LoadApplicantAsync(userId);
                if (applicant == null)
                {
                    return NotFound("Applicant not found");
                }

                // Looked up inside the caller's own applicant, so an id from
                // another account cannot be reached however it was obtained.
                var cv = applicant.CVVersions.FirstOrDefault(c => c.Id == request.CvVersionId);
                if (cv == null)
                {
                    return NotFound("That CV version was not found.");
                }

                if (string.IsNullOrEmpty(cv.BlobPath))
                {
                    return BadRequest("That CV version has no file attached, so there is nothing to read.");
                }

                var state = AiUsageWindow.Current(
                    new AiUsageState(applicant.AiCreditsPeriodStartUtc, applicant.AiCreditsUsed),
                    options.Period,
                    DateTime.UtcNow);

                if (!AiUsageWindow.HasRoom(state, options.CallsPerPeriod))
                {
                    return StatusCode(429,
                        $"You have used all {options.CallsPerPeriod} AI requests for this period. " +
                        $"The allowance renews on {AiUsageWindow.RenewsAt(state, options.Period):yyyy-MM-dd}.");
                }

                var bytes = await _blobs.DownloadAsync(cv.BlobPath, options.MaxCvBytes, HttpContext.RequestAborted);
                if (bytes == null)
                {
                    return NotFound("The file for that CV version could not be read.");
                }

                var (content, extractError) = CvContentExtractor.Extract(cv.FileName, bytes);
                if (content == null)
                {
                    return BadRequest(extractError);
                }

                // The credit is spent before the call, not after.
                //
                // Counting afterwards looks fairer and is not: several requests
                // arriving together would each read the same count, each find
                // room, and the allowance would bound nothing. Counting first
                // means a provider outage can cost someone a credit, which is
                // the smaller harm and the rarer one - and it is why every
                // check that can fail locally happens above this line.
                applicant.AiCreditsPeriodStartUtc = state.PeriodStartUtc;
                applicant.AiCreditsUsed = state.Used + 1;
                await _context.SaveChangesAsync();

                var spent = new AiUsageState(applicant.AiCreditsPeriodStartUtc, applicant.AiCreditsUsed);

                var (result, error) = await _tailoring.TailorAsync(
                    new CvTailoringInput(content, request), HttpContext.RequestAborted);

                if (result == null)
                {
                    return StatusCode(502, error);
                }

                result.Usage = ToDto(spent, options);

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error tailoring CV {CvId}", request.CvVersionId);
                return StatusCode(500, "An error occurred while preparing the analysis");
            }
        }

        private static AiUsageDto ToDto(AiUsageState state, CvTailoringOptions options) => new()
        {
            Used = state.Used,
            Limit = options.CallsPerPeriod,
            Remaining = AiUsageWindow.Remaining(state, options.CallsPerPeriod),
            RenewsAtUtc = AiUsageWindow.RenewsAt(state, options.Period)
        };
    }
}
