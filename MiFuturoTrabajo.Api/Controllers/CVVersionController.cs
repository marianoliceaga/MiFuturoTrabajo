using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiFuturoTrabajo.Infrastructure;
using MiFuturoTrabajo.Infrastructure.Entities;
using MiFuturoTrabajo.Shared.Models;
using MiFuturoTrabajo.Api.Helpers;
using MiFuturoTrabajo.Api.Services;

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
    public class CVVersionController : ControllerBase
    {
        private readonly ILogger<CVVersionController> _logger;
        private readonly ApplicationDbContext _context;

        public CVVersionController(ILogger<CVVersionController> logger, ApplicationDbContext context)
        {
            _logger = logger;
            _context = context;
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

                cv.Name = cvVersionDto.Name;
                cv.FileUrl = cvVersionDto.FileUrl;

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

                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting CV version {Id}", id);
                return StatusCode(500, "An error occurred while deleting the CV version");
            }
        }
    }
}
