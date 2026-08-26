using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiFuturoTrabajo.Infrastructure;
using MiFuturoTrabajo.Shared.Models;
using MiFuturoTrabajo.Api.Helpers;
using MiFuturoTrabajo.Api.Services;
using Entities = MiFuturoTrabajo.Infrastructure.Entities;
using Microsoft.AspNetCore.RateLimiting;

namespace MiFuturoTrabajo.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    [EnableRateLimiting(RateLimiting.PerUserPolicy)]
    public class CompaniesController : ControllerBase
    {
        private readonly ILogger<CompaniesController> _logger;
        private readonly ApplicationDbContext _context;
        private readonly QuotaService _quotas;

        public CompaniesController(ILogger<CompaniesController> logger, ApplicationDbContext context, QuotaService quotas)
        {
            _logger = logger;
            _context = context;
            _quotas = quotas;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            _logger.LogInformation("Getting all companies");

            var userId = AuthHelper.GetUserIdFromClaims(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("Unauthorized");
            }

            try
            {
                var companies = await _context.Companies
                    .Where(c => c.UserId == userId)
                    .ToListAsync();

                var companiesDto = companies.Select(c => c.ToDto()).ToList();

                return Ok(companiesDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting companies");
                return StatusCode(500, "An error occurred while retrieving companies");
            }
        }

        [HttpGet("CompanyGetById/{id}")]
        public async Task<IActionResult> GetById(string id)
        {
            _logger.LogInformation($"Getting company with ID: {id}");

            var userId = AuthHelper.GetUserIdFromClaims(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("Unauthorized");
            }

            try
            {
                var company = await _context.Companies
                    .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);

                if (company == null)
                {
                    return NotFound($"Company with ID {id} not found");
                }

                var companyDto = company.ToDto();

                return Ok(companyDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting company with ID: {id}");
                return StatusCode(500, "An error occurred while retrieving the company");
            }
        }

        [HttpPost("CompanyAdd")]
        public async Task<IActionResult> Create([FromBody] CompanyDto companyDto)
        {
            _logger.LogInformation("Creating new company");

            var userId = AuthHelper.GetUserIdFromClaims(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("Unauthorized");
            }

            try
            {
                if (companyDto == null)
                {
                    return BadRequest("Invalid company data");
                }

                var company = companyDto.ToEntity(userId);

                // Registration is open on a shared free-tier database, so one
                // account cannot be allowed to fill it for everybody else.
                var quotaError = await _quotas.CheckCompaniesAsync(userId, HttpContext.RequestAborted);
                if (quotaError != null)
                {
                    return StatusCode(429, new { error = quotaError });
                }

                _context.Companies.Add(company);
                await _context.SaveChangesAsync();

                var createdDto = company.ToDto();

                return StatusCode(201, createdDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating company");
                return StatusCode(500, "An error occurred while creating the company");
            }
        }

        [HttpPatch("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] CompanyDto companyDto)
        {
            _logger.LogInformation($"Updating company with ID: {id}");

            var userId = AuthHelper.GetUserIdFromClaims(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("Unauthorized");
            }

            try
            {
                var company = await _context.Companies
                    .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);

                if (company == null)
                {
                    return NotFound($"Company with ID {id} not found");
                }

                if (companyDto == null)
                {
                    return BadRequest("Invalid company data");
                }

                // Update properties
                var previousName = company.Name;
                company.Name = companyDto.Name ?? company.Name;
                company.Description = companyDto.Description ?? company.Description;
                company.Website = companyDto.Website ?? company.Website;
                company.Telephone = companyDto.Telephone ?? company.Telephone;
                company.Address = companyDto.Address ?? company.Address;
                company.Country = companyDto.Country ?? company.Country;
                company.Labels = new List<string>(companyDto.Labels);

                await _context.SaveChangesAsync();

                // The company name is denormalised into every job application
                // document so the board can render in a single query. When the
                // name changes, fan the new value out. Bounded: one
                // single-partition query over this user's own applications.
                if (!string.Equals(previousName, company.Name, StringComparison.Ordinal))
                {
                    await FanOutCompanyRenameAsync(userId, company.Id, company.Name);
                }

                var updatedDto = company.ToDto();

                return Ok(updatedDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating company with ID: {id}");
                return StatusCode(500, "An error occurred while updating the company");
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            _logger.LogInformation($"Deleting company with ID: {id}");

            var userId = AuthHelper.GetUserIdFromClaims(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("Unauthorized");
            }

            try
            {
                var company = await _context.Companies
                    .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);

                if (company == null)
                {
                    return NotFound($"Company with ID {id} not found");
                }

                _context.Companies.Remove(company);
                await _context.SaveChangesAsync();

                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting company with ID: {id}");
                return StatusCode(500, "An error occurred while deleting the company");
            }
        }

        /// <summary>
        /// Propagates a company rename into the CompanySnapshot embedded in
        /// each of this user's job applications.
        ///
        /// This is the maintenance cost of the denormalisation that lets the
        /// board render from one single-partition query instead of a
        /// cross-container join Cosmos cannot perform. It is bounded by the
        /// number of applications a single user has.
        /// </summary>
        private async Task FanOutCompanyRenameAsync(string userId, string companyId, string newName)
        {
            var applicant = await _context.Applicants
                .FirstOrDefaultAsync(a => a.UserId == userId);

            if (applicant == null)
            {
                return;
            }

            var affected = await _context.JobApplications
                .Where(ja => ja.ApplicantId == applicant.Id)
                .Where(j => j.CompanyId == companyId)
                .ToListAsync();

            if (affected.Count == 0)
            {
                return;
            }

            foreach (var application in affected)
            {
                application.Company ??= new Entities.CompanySnapshot { Id = companyId };
                application.Company.Name = newName;
            }

            await _context.SaveChangesAsync();
            _logger.LogInformation(
                "Company rename fanned out to {Count} job application(s)", affected.Count);
        }
    }
}
