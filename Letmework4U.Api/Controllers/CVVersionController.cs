using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Letmework4U.Infrastructure;
using Letmework4U.Infrastructure.Entities;
using Letmework4U.Shared.Models;
using Letmework4U.Api.Helpers;
using Letmework4U.Api.Services;

namespace Letmework4U.Api.Controllers
{
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

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            _logger.LogInformation("Getting all CV versions");

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

                var cvVersions = await _context.CVVersions
                    .Where(cv => cv.ApplicantId == applicant.Id)
                    .OrderByDescending(cv => cv.IsDefault)
                    .ThenByDescending(cv => cv.UploadedDate)
                    .ToListAsync();

                var cvVersionsDto = cvVersions.Select(cv => cv.ToDto()).ToList();

                return Ok(cvVersionsDto);
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
            _logger.LogInformation($"Getting CV version with ID: {id}");

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

                var cvVersion = await _context.CVVersions
                    .FirstOrDefaultAsync(cv => cv.Id == id && cv.ApplicantId == applicant.Id);

                if (cvVersion == null)
                {
                    return NotFound($"CV version with ID {id} not found");
                }

                var cvVersionDto = cvVersion.ToDto();

                return Ok(cvVersionDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting CV version with ID: {id}");
                return StatusCode(500, "An error occurred while retrieving the CV version");
            }
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CVVersionDto cvVersionDto)
        {
            _logger.LogInformation("Creating new CV version");

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

                if (cvVersionDto == null)
                {
                    return BadRequest("Invalid CV version data");
                }

                if (string.IsNullOrWhiteSpace(cvVersionDto.Name) || string.IsNullOrWhiteSpace(cvVersionDto.FileUrl))
                {
                    return BadRequest("Name and FileUrl are required");
                }

                // Check if this is the first CV - make it default
                var isFirstCV = !await _context.CVVersions.AnyAsync(cv => cv.ApplicantId == applicant.Id);
                var isDefault = cvVersionDto.IsDefault || isFirstCV;

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
                    Name = cvVersionDto.Name,
                    FileUrl = cvVersionDto.FileUrl,
                    UploadedDate = DateTime.UtcNow,
                    IsDefault = isDefault
                };

                _context.CVVersions.Add(cvVersion);
                await _context.SaveChangesAsync();

                var createdDto = cvVersion.ToDto();

                return StatusCode(201, createdDto);
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
            _logger.LogInformation($"Updating CV version with ID: {id}");

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

                var cvVersion = await _context.CVVersions
                    .FirstOrDefaultAsync(cv => cv.Id == id && cv.ApplicantId == applicant.Id);

                if (cvVersion == null)
                {
                    return NotFound($"CV version with ID {id} not found");
                }

                if (cvVersionDto == null)
                {
                    return BadRequest("Invalid CV version data");
                }

                // Update properties
                cvVersion.Name = cvVersionDto.Name ?? cvVersion.Name;
                cvVersion.FileUrl = cvVersionDto.FileUrl ?? cvVersion.FileUrl;

                // Handle IsDefault change
                if (cvVersionDto.IsDefault && !cvVersion.IsDefault)
                {
                    // Unset all other CVs as default
                    var existingDefaultCVs = await _context.CVVersions
                        .Where(cv => cv.ApplicantId == applicant.Id && cv.IsDefault && cv.Id != id)
                        .ToListAsync();

                    foreach (var existingCV in existingDefaultCVs)
                    {
                        existingCV.IsDefault = false;
                    }

                    cvVersion.IsDefault = true;
                }

                await _context.SaveChangesAsync();

                var updatedDto = cvVersion.ToDto();

                return Ok(updatedDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating CV version with ID: {id}");
                return StatusCode(500, "An error occurred while updating the CV version");
            }
        }

        [HttpPatch("{id}/setDefault")]
        public async Task<IActionResult> SetDefault(string id)
        {
            _logger.LogInformation($"Setting CV {id} as default");

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
                    .FirstOrDefaultAsync(c => c.Id == id && c.ApplicantId == applicant.Id);

                if (cv == null)
                {
                    return NotFound($"CV with ID {id} not found");
                }

                // Unset all other CVs as default
                var existingDefaultCVs = await _context.CVVersions
                    .Where(c => c.ApplicantId == applicant.Id && c.IsDefault && c.Id != id)
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
                _logger.LogError(ex, $"Error setting CV {id} as default");
                return StatusCode(500, "An error occurred while setting the default CV");
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            _logger.LogInformation($"Deleting CV version with ID: {id}");

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

                var cvVersion = await _context.CVVersions
                    .FirstOrDefaultAsync(cv => cv.Id == id && cv.ApplicantId == applicant.Id);

                if (cvVersion == null)
                {
                    return NotFound($"CV version with ID {id} not found");
                }

                _context.CVVersions.Remove(cvVersion);
                await _context.SaveChangesAsync();

                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting CV version with ID: {id}");
                return StatusCode(500, "An error occurred while deleting the CV version");
            }
        }
    }
}
