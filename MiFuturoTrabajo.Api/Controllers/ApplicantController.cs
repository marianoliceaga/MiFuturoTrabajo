using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiFuturoTrabajo.Infrastructure;
using MiFuturoTrabajo.Infrastructure.Entities;
using MiFuturoTrabajo.Shared.Models;
using MiFuturoTrabajo.Api.Helpers;
using MiFuturoTrabajo.Api.Services;
using Newtonsoft.Json;

namespace MiFuturoTrabajo.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ApplicantController : ControllerBase
    {
        private readonly ILogger<ApplicantController> _logger;
        private readonly ApplicationDbContext _context;
        private readonly IBlobStorageService _blobStorageService;

        public ApplicantController(ILogger<ApplicantController> logger, ApplicationDbContext context, IBlobStorageService blobStorageService)
        {
            _logger = logger;
            _context = context;
            _blobStorageService = blobStorageService;
        }

        [HttpGet("ApplicationGetById/{id}")]
        public async Task<IActionResult> GetById(string id)
        {
            _logger.LogInformation($"Getting applicant with ID: {id}");

            var userId = AuthHelper.GetUserIdFromClaims(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("Unauthorized");
            }

            try
            {
                // CVVersions and PastJobReferences are embedded in this
                // document, so they arrive with it - no Include required.
                var applicant = await _context.Applicants
                    .Where(a => a.UserId == userId)
                    .FirstOrDefaultAsync(a => a.Id == id);

                if (applicant == null)
                {
                    return NotFound($"Applicant with ID {id} not found");
                }

                var applicantDto = applicant.ToDto();

                return Ok(applicantDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting applicant with ID: {id}");
                return StatusCode(500, "An error occurred while retrieving the applicant");
            }
        }

        [HttpGet("current")]
        public async Task<IActionResult> GetCurrent()
        {
            _logger.LogInformation("Getting current applicant");

            var userId = AuthHelper.GetUserIdFromClaims(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("Unauthorized");
            }

            try
            {
                var applicant = await _context.Applicants
                    .Where(a => a.UserId == userId)
                    .FirstOrDefaultAsync();

                if (applicant == null)
                {
                    // Auto-create applicant on first login
                    applicant = new Applicant
                    {
                        Id = Guid.NewGuid().ToString(),
                        UserId = userId,
                        CreatedOnDate = DateTime.UtcNow
                    };

                    _context.Applicants.Add(applicant);
                    await _context.SaveChangesAsync();

                    _logger.LogInformation($"Auto-created applicant for user {userId}");
                }

                var applicantDto = applicant.ToDto();

                return Ok(applicantDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting current applicant");
                return StatusCode(500, "An error occurred while retrieving the applicant");
            }
        }

        [HttpPatch("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] ApplicantDto applicantDto)
        {
            _logger.LogInformation($"Updating applicant with ID: {id}");

            var userId = AuthHelper.GetUserIdFromClaims(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("Unauthorized");
            }

            try
            {
                var applicant = await _context.Applicants
                    .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId);

                if (applicant == null)
                {
                    return NotFound($"Applicant with ID {id} not found");
                }

                if (applicantDto == null)
                {
                    return BadRequest("Invalid applicant data");
                }

                // Update properties
                applicant.Name = applicantDto.Name ?? applicant.Name;
                applicant.Lastname = applicantDto.Lastname ?? applicant.Lastname;
                applicant.CurrentJob = applicantDto.CurrentJob ?? applicant.CurrentJob;
                applicant.LinkedInUrl = applicantDto.LinkedInUrl ?? applicant.LinkedInUrl;
                applicant.ContactEmail = applicantDto.ContactEmail ?? applicant.ContactEmail;
                applicant.Cellphone = applicantDto.Cellphone ?? applicant.Cellphone;

                await _context.SaveChangesAsync();

                var updatedDto = applicant.ToDto();

                return Ok(updatedDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating applicant with ID: {id}");
                return StatusCode(500, "An error occurred while updating the applicant");
            }
        }

        [HttpPost("UploadCV")]
        public async Task<IActionResult> UploadCV([FromForm] IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("No file uploaded.");
            }

            var userId = AuthHelper.GetUserIdFromClaims(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            try
            {
                var applicant = await _context.Applicants.FirstOrDefaultAsync(a => a.UserId == userId);
                if (applicant == null)
                {
                    return NotFound("Applicant not found");
                }

                using var stream = file.OpenReadStream();
                var fileUrl = await _blobStorageService.UploadFileAsync(stream, file.FileName, file.ContentType);

                var cvVersion = new CVVersion
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = file.FileName,
                    FileUrl = fileUrl,
                    UploadedDate = DateTime.UtcNow,
                    VersionNotes = "Uploaded via blob storage",
                    // First CV uploaded becomes the default.
                    IsDefault = applicant.CVVersions.Count == 0
                };

                applicant.CVVersions.Add(cvVersion);
                await _context.SaveChangesAsync();

                return Ok(cvVersion.ToDto());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upload CV blob");
                return StatusCode(500, "An error occurred while uploading CV.");
            }
        }

        [HttpPatch("CV/{cvId}/setDefault")]
        public async Task<IActionResult> SetDefaultCV(string cvId)
        {
            _logger.LogInformation($"Setting CV {cvId} as default");

            var userId = AuthHelper.GetUserIdFromClaims(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("Unauthorized");
            }

            try
            {
                var applicant = await _context.Applicants
                    .FirstOrDefaultAsync(a => a.UserId == userId);

                if (applicant == null)
                {
                    return NotFound("Applicant not found");
                }

                var cv = applicant.CVVersions.FirstOrDefault(c => c.Id == cvId);

                if (cv == null)
                {
                    return NotFound($"CV with ID {cvId} not found");
                }

                // One document, one write: the collection can never end up
                // with two defaults.
                foreach (var existingCV in applicant.CVVersions)
                {
                    existingCV.IsDefault = false;
                }

                cv.IsDefault = true;
                await _context.SaveChangesAsync();

                var cvDto = cv.ToDto();

                return Ok(cvDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error setting CV {cvId} as default");
                return StatusCode(500, "An error occurred while setting the default CV");
            }
        }

        [HttpDelete("CV/{cvId}")]
        public async Task<IActionResult> DeleteCV(string cvId)
        {
            _logger.LogInformation($"Deleting CV {cvId}");

            var userId = AuthHelper.GetUserIdFromClaims(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("Unauthorized");
            }

            try
            {
                var applicant = await _context.Applicants
                    .FirstOrDefaultAsync(a => a.UserId == userId);

                if (applicant == null)
                {
                    return NotFound("Applicant not found");
                }

                var cv = applicant.CVVersions.FirstOrDefault(c => c.Id == cvId);

                if (cv == null)
                {
                    return NotFound($"CV with ID {cvId} not found");
                }

                var wasDefault = cv.IsDefault;
                applicant.CVVersions.Remove(cv);

                // Never leave the applicant without a default CV.
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
                _logger.LogError(ex, $"Error deleting CV {cvId}");
                return StatusCode(500, "An error occurred while deleting the CV");
            }
        }

        [HttpPost("AddReference")]
        public async Task<IActionResult> AddReference([FromBody] JobReferenceDto referenceDto)
        {
            _logger.LogInformation("Adding job reference");

            var userId = AuthHelper.GetUserIdFromClaims(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("Unauthorized");
            }

            try
            {
                var applicant = await _context.Applicants
                    .FirstOrDefaultAsync(a => a.UserId == userId);

                if (applicant == null)
                {
                    return NotFound("Applicant not found");
                }

                if (referenceDto == null)
                {
                    return BadRequest("Invalid reference data");
                }

                if (string.IsNullOrWhiteSpace(referenceDto.CompanyName))
                {
                    return BadRequest("CompanyName is required");
                }

                if (string.IsNullOrWhiteSpace(referenceDto.JobTitle))
                {
                    return BadRequest("JobTitle is required");
                }

                var reference = referenceDto.ToEntity();

                applicant.PastJobReferences.Add(reference);
                await _context.SaveChangesAsync();

                var createdDto = reference.ToDto();

                return StatusCode(201, createdDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding job reference");
                return StatusCode(500, "An error occurred while adding the reference");
            }
        }

        [HttpDelete("DeleteReference/{referenceId}")]
        public async Task<IActionResult> DeleteReference(string referenceId)
        {
            _logger.LogInformation($"Deleting job reference {referenceId}");

            var userId = AuthHelper.GetUserIdFromClaims(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("Unauthorized");
            }

            try
            {
                var applicant = await _context.Applicants
                    .FirstOrDefaultAsync(a => a.UserId == userId);

                if (applicant == null)
                {
                    return NotFound("Applicant not found");
                }

                var reference = applicant.PastJobReferences
                    .FirstOrDefault(r => r.Id == referenceId);

                if (reference == null)
                {
                    return NotFound($"Reference with ID {referenceId} not found");
                }

                applicant.PastJobReferences.Remove(reference);
                await _context.SaveChangesAsync();

                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting job reference {referenceId}");
                return StatusCode(500, "An error occurred while deleting the reference");
            }
        }
    }
}
