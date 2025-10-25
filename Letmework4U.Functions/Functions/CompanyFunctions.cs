using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Net;
using Letmework4U.Infrastructure;
using Letmework4U.Infrastructure.Entities;
using Letmework4U.Shared.Models;
using Letmework4U.Functions.Helpers;
using Letmework4U.Functions.Services;
using Newtonsoft.Json;

namespace Letmework4U.Functions.Functions
{
    public class CompanyFunctions
    {
        private readonly ILogger<CompanyFunctions> _logger;
        private readonly ApplicationDbContext _context;

        public CompanyFunctions(ILogger<CompanyFunctions> logger, ApplicationDbContext context)
        {
            _logger = logger;
            _context = context;
        }

        [Function("Companies")]
        public async Task<HttpResponseData> GetAll(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "Companies")] HttpRequestData req)
        {
            _logger.LogInformation("Getting all companies");

            var userId = AuthHelper.GetUserIdFromRequest(req);
            if (string.IsNullOrEmpty(userId))
            {
                var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorizedResponse.WriteStringAsync("Unauthorized");
                return unauthorizedResponse;
            }

            try
            {
                var companies = await _context.Companies
                    .Where(c => c.UserId == userId)
                    .ToListAsync();

                var companiesDto = companies.Select(c => c.ToDto()).ToList();

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(companiesDto);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting companies");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("An error occurred while retrieving companies");
                return errorResponse;
            }
        }

        [Function("CompanyGetById")]
        public async Task<HttpResponseData> GetById(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "Companies/CompanyGetById/{id}")] HttpRequestData req,
            string id)
        {
            _logger.LogInformation($"Getting company with ID: {id}");

            var userId = AuthHelper.GetUserIdFromRequest(req);
            if (string.IsNullOrEmpty(userId))
            {
                var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorizedResponse.WriteStringAsync("Unauthorized");
                return unauthorizedResponse;
            }

            try
            {
                var company = await _context.Companies
                    .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);

                if (company == null)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteStringAsync($"Company with ID {id} not found");
                    return notFoundResponse;
                }

                var companyDto = company.ToDto();

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(companyDto);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting company with ID: {id}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("An error occurred while retrieving the company");
                return errorResponse;
            }
        }

        [Function("CompanyAdd")]
        public async Task<HttpResponseData> Create(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "Companies/CompanyAdd")] HttpRequestData req)
        {
            _logger.LogInformation("Creating new company");

            var userId = AuthHelper.GetUserIdFromRequest(req);
            if (string.IsNullOrEmpty(userId))
            {
                var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorizedResponse.WriteStringAsync("Unauthorized");
                return unauthorizedResponse;
            }

            try
            {
                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var companyDto = JsonConvert.DeserializeObject<CompanyDto>(requestBody);

                if (companyDto == null)
                {
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteStringAsync("Invalid company data");
                    return badRequestResponse;
                }

                var company = companyDto.ToEntity(userId);

                _context.Companies.Add(company);
                await _context.SaveChangesAsync();

                var createdDto = company.ToDto();

                var response = req.CreateResponse(HttpStatusCode.Created);
                await response.WriteAsJsonAsync(createdDto);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating company");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("An error occurred while creating the company");
                return errorResponse;
            }
        }

        [Function("CompanyUpdate")]
        public async Task<HttpResponseData> Update(
            [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "Companies/{id}")] HttpRequestData req,
            string id)
        {
            _logger.LogInformation($"Updating company with ID: {id}");

            var userId = AuthHelper.GetUserIdFromRequest(req);
            if (string.IsNullOrEmpty(userId))
            {
                var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorizedResponse.WriteStringAsync("Unauthorized");
                return unauthorizedResponse;
            }

            try
            {
                var company = await _context.Companies
                    .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);

                if (company == null)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteStringAsync($"Company with ID {id} not found");
                    return notFoundResponse;
                }

                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var companyDto = JsonConvert.DeserializeObject<CompanyDto>(requestBody);

                if (companyDto == null)
                {
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteStringAsync("Invalid company data");
                    return badRequestResponse;
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

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(updatedDto);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating company with ID: {id}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("An error occurred while updating the company");
                return errorResponse;
            }
        }

        [Function("CompanyDelete")]
        public async Task<HttpResponseData> Delete(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "Companies/{id}")] HttpRequestData req,
            string id)
        {
            _logger.LogInformation($"Deleting company with ID: {id}");

            var userId = AuthHelper.GetUserIdFromRequest(req);
            if (string.IsNullOrEmpty(userId))
            {
                var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorizedResponse.WriteStringAsync("Unauthorized");
                return unauthorizedResponse;
            }

            try
            {
                var company = await _context.Companies
                    .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);

                if (company == null)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteStringAsync($"Company with ID {id} not found");
                    return notFoundResponse;
                }

                _context.Companies.Remove(company);
                await _context.SaveChangesAsync();

                var response = req.CreateResponse(HttpStatusCode.NoContent);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting company with ID: {id}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("An error occurred while deleting the company");
                return errorResponse;
            }
        }
    }
}
