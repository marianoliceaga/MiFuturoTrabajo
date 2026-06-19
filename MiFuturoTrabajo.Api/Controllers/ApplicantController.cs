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

        public ApplicantController(ILogger<ApplicantController> logger, ApplicationDbContext context)
        {
            _logger = logger;
            _context = context;
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
                var applicant = await _context.Applicants
                    .Include(a => a.CVVersions)
                    .Include(a => a.PastJobReferences)
                    .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId);

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
                    .Include(a => a.CVVersions)
                    .Include(a => a.PastJobReferences)
                    .FirstOrDefaultAsync(a => a.UserId == userId);

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
        public async Task<IActionResult> UploadCV([FromBody] Dictionary<string, string> cvData)
        {
            _logger.LogInformation("Uploading CV");

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

                if (cvData == null || !cvData.ContainsKey("name") || !cvData.ContainsKey("fileUrl"))
                {
                    return BadRequest("Invalid CV data. Required: name, fileUrl");
                }

                // Check if this is the first CV - make it default
                var isFirstCV = !await _context.CVVersions.AnyAsync(cv => cv.ApplicantId == applicant.Id);
                var isDefault = cvData.ContainsKey("isDefault")
                    ? bool.Parse(cvData["isDefault"])
                    : isFirstCV;

                // If setting as default, unset all other CVs
                if (isDefault)
                {
                    var existingCVs = await _context.CVVersions
                        .Where(cv => cv.ApplicantId == applicant.Id && cv.IsDefault)
                        .ToListAsync();

                    foreach (var cv in existingCVs)
                    {
                        cv.IsDefault = false;
                    }
                }

                var cvVersion = new CVVersion
                {
                    Id = Guid.NewGuid().ToString(),
                    ApplicantId = applicant.Id,
                    Name = cvData["name"],
                    FileUrl = cvData["fileUrl"],
                    UploadedDate = DateTime.UtcNow,
                    IsDefault = isDefault
                };

                _context.CVVersions.Add(cvVersion);
                await _context.SaveChangesAsync();

                var cvDto = cvVersion.ToDto();

                return StatusCode(201, cvDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading CV");
                return StatusCode(500, "An error occurred while uploading the CV");
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

                var cv = await _context.CVVersions
                    .FirstOrDefaultAsync(c => c.Id == cvId && c.ApplicantId == applicant.Id);

                if (cv == null)
                {
                    return NotFound($"CV with ID {cvId} not found");
                }

                // Unset all other CVs as default
                var existingDefaultCVs = await _context.CVVersions
                    .Where(c => c.ApplicantId == applicant.Id && c.IsDefault && c.Id != cvId)
                    .ToListAsync();

                foreach (var existingCV in existingDefaultCVs)
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

                var cv = await _context.CVVersions
                    .FirstOrDefaultAsync(c => c.Id == cvId && c.ApplicantId == applicant.Id);

                if (cv == null)
                {
                    return NotFound($"CV with ID {cvId} not found");
                }

                _context.CVVersions.Remove(cv);
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

                var reference = referenceDto.ToEntity(applicant.Id);

                _context.JobReferences.Add(reference);
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

                var reference = await _context.JobReferences
                    .FirstOrDefaultAsync(r => r.Id == referenceId && r.ApplicantId == applicant.Id);

                if (reference == null)
                {
                    return NotFound($"Reference with ID {referenceId} not found");
                }

                _context.JobReferences.Remove(reference);
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
