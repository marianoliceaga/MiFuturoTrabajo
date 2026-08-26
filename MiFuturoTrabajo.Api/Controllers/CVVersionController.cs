using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiFuturoTrabajo.Infrastructure;
using MiFuturoTrabajo.Infrastructure.Entities;
using MiFuturoTrabajo.Shared.Models;
using MiFuturoTrabajo.Api.Helpers;
using MiFuturoTrabajo.Api.Services;
using Microsoft.AspNetCore.RateLimiting;

namespace MiFuturoTrabajo.Api.Controllers
{
    /// <summary>
    /// CV versions are embedded in the Applicant document rather than stored
    /// in their own container, so every action here is: load the applicant
    /// with one single-partition query, mutate the embedded collection, save.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    [EnableRateLimiting(RateLimiting.PerUserPolicy)]
    public class CVVersionController : ControllerBase
    {
        private readonly ILogger<CVVersionController> _logger;
        private readonly ApplicationDbContext _context;
        private readonly ICvDocumentService _cvDocuments;

        public CVVersionController(
            ILogger<CVVersionController> logger,
            ApplicationDbContext context,
            ICvDocumentService cvDocuments)
        {
            _logger = logger;
            _context = context;
            _cvDocuments = cvDocuments;
        }

        /// <summary>Applicants are partitioned by UserId, so this is a single-partition read.</summary>
        private Task<Applicant?> LoadApplicantAsync(string userId) =>
            _context.Applicants.Where(a => a.UserId == userId).FirstOrDefaultAsync();

        private static IEnumerable<CVVersion> Ordered(Applicant applicant) =>
            applicant.CVVersions
                .OrderByDescending(cv => cv.IsDefault)
                .ThenByDescending(cv => cv.UploadedDate);

        [HttpGet]
        public async Task<IActionResult> GetAll()
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

                return Ok(Ordered(applicant).Select(cv => cv.ToDto()).ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting CV versions");
                return StatusCode(500, "An error occurred while retrieving CV versions");
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id)
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

                var cv = applicant.CVVersions.FirstOrDefault(c => c.Id == id);
                if (cv == null)
                {
                    return NotFound($"CV version with ID {id} not found");
                }

                return Ok(cv.ToDto());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting CV version {Id}", id);
                return StatusCode(500, "An error occurred while retrieving the CV version");
            }
        }

        /// <summary>
        /// Records a CV version without a document. The file arrives separately
        /// through <see cref="Upload"/>, because a storage key is something this
        /// API assigns - never something a request body supplies.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CVVersionDto cvVersionDto)
        {
            var userId = AuthHelper.GetUserIdFromClaims(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("Unauthorized");
            }

            if (cvVersionDto == null)
            {
                return BadRequest("Invalid CV version data");
            }

            try
            {
                var applicant = await LoadApplicantAsync(userId);
                if (applicant == null)
                {
                    return NotFound("Applicant not found");
                }

                var cv = cvVersionDto.ToEntity();

                // First CV uploaded becomes the default automatically.
                if (applicant.CVVersions.Count == 0)
                {
                    cv.IsDefault = true;
                }
                else if (cv.IsDefault)
                {
                    foreach (var other in applicant.CVVersions)
                    {
                        other.IsDefault = false;
                    }
                }

                applicant.CVVersions.Add(cv);
                await _context.SaveChangesAsync();

                return StatusCode(201, cv.ToDto());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating CV version");
                return StatusCode(500, "An error occurred while creating the CV version");
            }
        }

        [HttpPatch("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] CVVersionDto cvVersionDto)
        {
            var userId = AuthHelper.GetUserIdFromClaims(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("Unauthorized");
            }

            if (cvVersionDto == null)
            {
                return BadRequest("Invalid CV version data");
            }

            try
            {
                var applicant = await LoadApplicantAsync(userId);
                if (applicant == null)
                {
                    return NotFound("Applicant not found");
                }

                var cv = applicant.CVVersions.FirstOrDefault(c => c.Id == id);
                if (cv == null)
                {
                    return NotFound($"CV version with ID {id} not found");
                }

                // Only the descriptive fields are editable. BlobPath, FileName,
                // ContentType and SizeBytes describe a blob this API stored, so
                // letting a PATCH body set them would let any user repoint their
                // own CV row at another user document.
                cv.Name = cvVersionDto.Name ?? cv.Name;
                cv.VersionNotes = cvVersionDto.VersionNotes ?? cv.VersionNotes;

                await _context.SaveChangesAsync();

                return Ok(cv.ToDto());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating CV version {Id}", id);
                return StatusCode(500, "An error occurred while updating the CV version");
            }
        }

        [HttpPatch("{id}/setDefault")]
        public async Task<IActionResult> SetDefault(string id)
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

                var cv = applicant.CVVersions.FirstOrDefault(c => c.Id == id);
                if (cv == null)
                {
                    return NotFound($"CV version with ID {id} not found");
                }

                // Embedding makes this atomic: one document, one write, so the
                // collection can never end up with two defaults.
                foreach (var other in applicant.CVVersions)
                {
                    other.IsDefault = false;
                }

                cv.IsDefault = true;
                await _context.SaveChangesAsync();

                return Ok(cv.ToDto());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting CV version {Id} as default", id);
                return StatusCode(500, "An error occurred while setting the default CV version");
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
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

                var cv = applicant.CVVersions.FirstOrDefault(c => c.Id == id);
                if (cv == null)
                {
                    return NotFound($"CV version with ID {id} not found");
                }

                var wasDefault = cv.IsDefault;
                applicant.CVVersions.Remove(cv);

                // Never leave the applicant without a default.
                if (wasDefault && applicant.CVVersions.Count > 0)
                {
                    applicant.CVVersions
                        .OrderByDescending(c => c.UploadedDate)
                        .First()
                        .IsDefault = true;
                }

                await _context.SaveChangesAsync();

                // Blob last: the record is already gone, so a storage hiccup
                // leaves a logged orphan rather than a delete the user cannot
                // complete.
                await _cvDocuments.RemoveAsync(cv, HttpContext.RequestAborted);

                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting CV version {Id}", id);
                return StatusCode(500, "An error occurred while deleting the CV version");
            }
        }

        /// <summary>
        /// Uploads a document and records it as a new CV version. Mirrors
        /// POST /api/applicant/UploadCV so both CV surfaces share one code path.
        /// </summary>
        [HttpPost("upload")]
        [RequestSizeLimit(CvFileValidator.MaxSizeBytes + 32 * 1024)]
        public async Task<IActionResult> Upload([FromForm] IFormFile file, [FromForm] string? name = null)
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

                var (cv, error) = await _cvDocuments.StoreAsync(userId, file, name, HttpContext.RequestAborted);
                if (cv == null)
                {
                    return BadRequest(error);
                }

                cv.IsDefault = applicant.CVVersions.Count == 0;

                applicant.CVVersions.Add(cv);
                await _context.SaveChangesAsync();

                return StatusCode(201, cv.ToDto());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading CV version");
                return StatusCode(500, "An error occurred while uploading the CV");
            }
        }

        /// <summary>
        /// Returns a link that is valid for a few minutes. The lookup happens
        /// inside the applicant belonging to the caller, so one account token
        /// can never mint a link to a different account document.
        /// </summary>
        [HttpGet("{id}/download")]
        public async Task<IActionResult> Download(string id)
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

                var cv = applicant.CVVersions.FirstOrDefault(c => c.Id == id);
                if (cv == null)
                {
                    return NotFound($"CV version with ID {id} not found");
                }

                var link = await _cvDocuments.CreateDownloadLinkAsync(cv, HttpContext.RequestAborted);
                if (link == null)
                {
                    return NotFound("This CV version has no file attached.");
                }

                return Ok(link);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating download link for CV version {Id}", id);
                return StatusCode(500, "An error occurred while preparing the download");
            }
        }
    }
}
