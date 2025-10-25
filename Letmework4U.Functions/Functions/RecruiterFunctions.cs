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
    public class RecruiterFunctions
    {
        private readonly ILogger<RecruiterFunctions> _logger;
        private readonly ApplicationDbContext _context;

        public RecruiterFunctions(ILogger<RecruiterFunctions> logger, ApplicationDbContext context)
        {
            _logger = logger;
            _context = context;
        }

        [Function("Recruiters")]
        public async Task<HttpResponseData> GetAll(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "Recruiters")] HttpRequestData req)
        {
            _logger.LogInformation("Getting all recruiters");

            var userId = AuthHelper.GetUserIdFromRequest(req);
            if (string.IsNullOrEmpty(userId))
            {
                var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorizedResponse.WriteStringAsync("Unauthorized");
                return unauthorizedResponse;
            }

            try
            {
                var recruiters = await _context.Recruiters
                    .Where(r => r.UserId == userId)
                    .ToListAsync();

                var recruitersDto = recruiters.Select(r => r.ToDto()).ToList();

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(recruitersDto);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting recruiters");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("An error occurred while retrieving recruiters");
                return errorResponse;
            }
        }

        [Function("RecruiterGetById")]
        public async Task<HttpResponseData> GetById(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "Recruiters/RecruiterGetById/{id}")] HttpRequestData req,
            string id)
        {
            _logger.LogInformation($"Getting recruiter with ID: {id}");

            var userId = AuthHelper.GetUserIdFromRequest(req);
            if (string.IsNullOrEmpty(userId))
            {
                var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorizedResponse.WriteStringAsync("Unauthorized");
                return unauthorizedResponse;
            }

            try
            {
                var recruiter = await _context.Recruiters
                    .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);

                if (recruiter == null)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteStringAsync($"Recruiter with ID {id} not found");
                    return notFoundResponse;
                }

                var recruiterDto = recruiter.ToDto();

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(recruiterDto);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting recruiter with ID: {id}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("An error occurred while retrieving the recruiter");
                return errorResponse;
            }
        }

        [Function("RecruiterAdd")]
        public async Task<HttpResponseData> Create(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "Recruiters/RecruiterAdd")] HttpRequestData req)
        {
            _logger.LogInformation("Creating new recruiter");

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
                var recruiterDto = JsonConvert.DeserializeObject<RecruiterDto>(requestBody);

                if (recruiterDto == null)
                {
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteStringAsync("Invalid recruiter data");
                    return badRequestResponse;
                }

                var recruiter = recruiterDto.ToEntity(userId);

                _context.Recruiters.Add(recruiter);
                await _context.SaveChangesAsync();

                var createdDto = recruiter.ToDto();

                var response = req.CreateResponse(HttpStatusCode.Created);
                await response.WriteAsJsonAsync(createdDto);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating recruiter");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("An error occurred while creating the recruiter");
                return errorResponse;
            }
        }

        [Function("RecruiterUpdate")]
        public async Task<HttpResponseData> Update(
            [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "Recruiters/{id}")] HttpRequestData req,
            string id)
        {
            _logger.LogInformation($"Updating recruiter with ID: {id}");

            var userId = AuthHelper.GetUserIdFromRequest(req);
            if (string.IsNullOrEmpty(userId))
            {
                var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorizedResponse.WriteStringAsync("Unauthorized");
                return unauthorizedResponse;
            }

            try
            {
                var recruiter = await _context.Recruiters
                    .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);

                if (recruiter == null)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteStringAsync($"Recruiter with ID {id} not found");
                    return notFoundResponse;
                }

                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var recruiterDto = JsonConvert.DeserializeObject<RecruiterDto>(requestBody);

                if (recruiterDto == null)
                {
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteStringAsync("Invalid recruiter data");
                    return badRequestResponse;
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

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(updatedDto);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating recruiter with ID: {id}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("An error occurred while updating the recruiter");
                return errorResponse;
            }
        }

        [Function("RecruiterDeleteById")]
        public async Task<HttpResponseData> Delete(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "Recruiters/RecruiterDeleteById/{id}")] HttpRequestData req,
            string id)
        {
            _logger.LogInformation($"Deleting recruiter with ID: {id}");

            var userId = AuthHelper.GetUserIdFromRequest(req);
            if (string.IsNullOrEmpty(userId))
            {
                var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorizedResponse.WriteStringAsync("Unauthorized");
                return unauthorizedResponse;
            }

            try
            {
                var recruiter = await _context.Recruiters
                    .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);

                if (recruiter == null)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteStringAsync($"Recruiter with ID {id} not found");
                    return notFoundResponse;
                }

                _context.Recruiters.Remove(recruiter);
                await _context.SaveChangesAsync();

                var response = req.CreateResponse(HttpStatusCode.NoContent);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting recruiter with ID: {id}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("An error occurred while deleting the recruiter");
                return errorResponse;
            }
        }
    }
}
