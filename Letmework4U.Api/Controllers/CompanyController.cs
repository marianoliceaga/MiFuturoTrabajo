using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Letmework4U.Infrastructure;
using Letmework4U.Shared.Models;
using Letmework4U.Api.Helpers;
using Letmework4U.Api.Services;
using Newtonsoft.Json;

namespace Letmework4U.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class CompanyController : ControllerBase
    {
        private readonly ILogger<CompanyController> _logger;
        private readonly ApplicationDbContext _context;

        public CompanyController(ILogger<CompanyController> logger, ApplicationDbContext context)
        {
            _logger = logger;
            _context = context;
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
                company.Name = companyDto.Name ?? company.Name;
                company.Description = companyDto.Description ?? company.Description;
                company.Website = companyDto.Website ?? company.Website;
                company.Telephone = companyDto.Telephone ?? company.Telephone;
                company.Address = companyDto.Address ?? company.Address;
                company.Country = companyDto.Country ?? company.Country;
                company.LabelsJson = JsonConvert.SerializeObject(companyDto.Labels ?? new List<string>());

                await _context.SaveChangesAsync();

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
    }
}
