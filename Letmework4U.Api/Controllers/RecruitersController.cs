using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Letmework4U.Infrastructure;
using Letmework4U.Shared.Models;
using Letmework4U.Api.Helpers;
using Letmework4U.Api.Services;

namespace Letmework4U.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class RecruitersController : ControllerBase
    {
        private readonly ILogger<RecruitersController> _logger;
        private readonly ApplicationDbContext _context;

        public RecruitersController(ILogger<RecruitersController> logger, ApplicationDbContext context)
        {
            _logger = logger;
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            _logger.LogInformation("Getting all recruiters");

            var userId = AuthHelper.GetUserIdFromClaims(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("Unauthorized");
            }

            try
            {
                var recruiters = await _context.Recruiters
                    .Where(r => r.UserId == userId)
                    .ToListAsync();

                var recruitersDto = recruiters.Select(r => r.ToDto()).ToList();

                return Ok(recruitersDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting recruiters");
                return StatusCode(500, "An error occurred while retrieving recruiters");
            }
        }

        [HttpGet("RecruiterGetById/{id}")]
        public async Task<IActionResult> GetById(string id)
        {
            _logger.LogInformation($"Getting recruiter with ID: {id}");

            var userId = AuthHelper.GetUserIdFromClaims(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("Unauthorized");
            }

            try
            {
                var recruiter = await _context.Recruiters
                    .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);

                if (recruiter == null)
                {
                    return NotFound($"Recruiter with ID {id} not found");
                }

                var recruiterDto = recruiter.ToDto();

                return Ok(recruiterDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting recruiter with ID: {id}");
                return StatusCode(500, "An error occurred while retrieving the recruiter");
            }
        }

        [HttpPost("RecruiterAdd")]
        public async Task<IActionResult> Create([FromBody] RecruiterDto recruiterDto)
        {
            _logger.LogInformation("Creating new recruiter");

            var userId = AuthHelper.GetUserIdFromClaims(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("Unauthorized");
            }

            try
            {
                if (recruiterDto == null)
                {
                    return BadRequest("Invalid recruiter data");
                }

                var recruiter = recruiterDto.ToEntity(userId);

                _context.Recruiters.Add(recruiter);
                await _context.SaveChangesAsync();

                var createdDto = recruiter.ToDto();

                return StatusCode(201, createdDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating recruiter");
                return StatusCode(500, "An error occurred while creating the recruiter");
            }
        }

        [HttpPatch("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] RecruiterDto recruiterDto)
        {
            _logger.LogInformation($"Updating recruiter with ID: {id}");

            var userId = AuthHelper.GetUserIdFromClaims(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("Unauthorized");
            }

            try
            {
                var recruiter = await _context.Recruiters
                    .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);

                if (recruiter == null)
                {
                    return NotFound($"Recruiter with ID {id} not found");
                }

                if (recruiterDto == null)
                {
                    return BadRequest("Invalid recruiter data");
                }

                // Update properties
                recruiter.Name = recruiterDto.Name ?? recruiter.Name;
                recruiter.Lastname = recruiterDto.Lastname ?? recruiter.Lastname;
                recruiter.ContactEmail = recruiterDto.ContactEmail ?? recruiter.ContactEmail;
                recruiter.Cellphone = recruiterDto.Cellphone ?? recruiter.Cellphone;
                recruiter.Landline = recruiterDto.Landline ?? recruiter.Landline;
                recruiter.LinkedInUrl = recruiterDto.LinkedInUrl ?? recruiter.LinkedInUrl;
                recruiter.IsFromConsultancy = recruiterDto.IsFromConsultancy;
                recruiter.CompanyName = recruiterDto.CompanyName ?? recruiter.CompanyName;

                await _context.SaveChangesAsync();

                var updatedDto = recruiter.ToDto();

                return Ok(updatedDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating recruiter with ID: {id}");
                return StatusCode(500, "An error occurred while updating the recruiter");
            }
        }

        [HttpDelete("RecruiterDeleteById/{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            _logger.LogInformation($"Deleting recruiter with ID: {id}");

            var userId = AuthHelper.GetUserIdFromClaims(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("Unauthorized");
            }

            try
            {
                var recruiter = await _context.Recruiters
                    .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);

                if (recruiter == null)
                {
                    return NotFound($"Recruiter with ID {id} not found");
                }

                _context.Recruiters.Remove(recruiter);
                await _context.SaveChangesAsync();

                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting recruiter with ID: {id}");
                return StatusCode(500, "An error occurred while deleting the recruiter");
            }
        }
    }
}
