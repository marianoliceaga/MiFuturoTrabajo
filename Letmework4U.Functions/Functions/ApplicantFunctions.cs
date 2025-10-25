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
    public class ApplicantFunctions
    {
        private readonly ILogger<ApplicantFunctions> _logger;
        private readonly ApplicationDbContext _context;

        public ApplicantFunctions(ILogger<ApplicantFunctions> logger, ApplicationDbContext context)
        {
            _logger = logger;
            _context = context;
        }

        [Function("ApplicationGetById")]
        public async Task<HttpResponseData> GetById(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "Applicants/ApplicationGetById/{id}")] HttpRequestData req,
            string id)
        {
            _logger.LogInformation($"Getting applicant with ID: {id}");

            var userId = AuthHelper.GetUserIdFromRequest(req);
            if (string.IsNullOrEmpty(userId))
            {
                var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorizedResponse.WriteStringAsync("Unauthorized");
                return unauthorizedResponse;
            }

            try
            {
                var applicant = await _context.Applicants
                    .Include(a => a.CVVersions)
                    .Include(a => a.PastJobReferences)
                    .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId);

                if (applicant == null)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteStringAsync($"Applicant with ID {id} not found");
                    return notFoundResponse;
                }

                var applicantDto = applicant.ToDto();

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(applicantDto);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting applicant with ID: {id}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("An error occurred while retrieving the applicant");
                return errorResponse;
            }
        }

        [Function("ApplicantGetCurrent")]
        public async Task<HttpResponseData> GetCurrent(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "Applicants/current")] HttpRequestData req)
        {
            _logger.LogInformation("Getting current applicant");

            var userId = AuthHelper.GetUserIdFromRequest(req);
            if (string.IsNullOrEmpty(userId))
            {
                var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorizedResponse.WriteStringAsync("Unauthorized");
                return unauthorizedResponse;
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

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(applicantDto);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting current applicant");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("An error occurred while retrieving the applicant");
                return errorResponse;
            }
        }

        [Function("ApplicantUpdate")]
        public async Task<HttpResponseData> Update(
            [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "Applicants/{id}")] HttpRequestData req,
            string id)
        {
            _logger.LogInformation($"Updating applicant with ID: {id}");

            var userId = AuthHelper.GetUserIdFromRequest(req);
            if (string.IsNullOrEmpty(userId))
            {
                var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorizedResponse.WriteStringAsync("Unauthorized");
                return unauthorizedResponse;
            }

            try
            {
                var applicant = await _context.Applicants
                    .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId);

                if (applicant == null)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteStringAsync($"Applicant with ID {id} not found");
                    return notFoundResponse;
                }

                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var applicantDto = JsonConvert.DeserializeObject<ApplicantDto>(requestBody);

                if (applicantDto == null)
                {
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteStringAsync("Invalid applicant data");
                    return badRequestResponse;
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

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(updatedDto);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating applicant with ID: {id}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("An error occurred while updating the applicant");
                return errorResponse;
            }
        }

        [Function("UploadCV")]
        public async Task<HttpResponseData> UploadCV(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "Applicants/UploadCV")] HttpRequestData req)
        {
            _logger.LogInformation("Uploading CV");

            var userId = AuthHelper.GetUserIdFromRequest(req);
            if (string.IsNullOrEmpty(userId))
            {
                var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorizedResponse.WriteStringAsync("Unauthorized");
                return unauthorizedResponse;
            }

            try
            {
                var applicant = await _context.Applicants
                    .FirstOrDefaultAsync(a => a.UserId == userId);

                if (applicant == null)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteStringAsync("Applicant not found");
                    return notFoundResponse;
                }

                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var cvData = JsonConvert.DeserializeObject<Dictionary<string, string>>(requestBody);

                if (cvData == null || !cvData.ContainsKey("name") || !cvData.ContainsKey("fileUrl"))
                {
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteStringAsync("Invalid CV data. Required: name, fileUrl");
                    return badRequestResponse;
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

                var response = req.CreateResponse(HttpStatusCode.Created);
                await response.WriteAsJsonAsync(cvDto);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading CV");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("An error occurred while uploading the CV");
                return errorResponse;
            }
        }

        [Function("SetDefaultCV")]
        public async Task<HttpResponseData> SetDefaultCV(
            [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "Applicants/CV/{cvId}/setDefault")] HttpRequestData req,
            string cvId)
        {
            _logger.LogInformation($"Setting CV {cvId} as default");

            var userId = AuthHelper.GetUserIdFromRequest(req);
            if (string.IsNullOrEmpty(userId))
            {
                var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorizedResponse.WriteStringAsync("Unauthorized");
                return unauthorizedResponse;
            }

            try
            {
                var applicant = await _context.Applicants
                    .FirstOrDefaultAsync(a => a.UserId == userId);

                if (applicant == null)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteStringAsync("Applicant not found");
                    return notFoundResponse;
                }

                var cv = await _context.CVVersions
                    .FirstOrDefaultAsync(c => c.Id == cvId && c.ApplicantId == applicant.Id);

                if (cv == null)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteStringAsync($"CV with ID {cvId} not found");
                    return notFoundResponse;
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

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(cvDto);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error setting CV {cvId} as default");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("An error occurred while setting the default CV");
                return errorResponse;
            }
        }

        [Function("DeleteCV")]
        public async Task<HttpResponseData> DeleteCV(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "Applicants/CV/{cvId}")] HttpRequestData req,
            string cvId)
        {
            _logger.LogInformation($"Deleting CV {cvId}");

            var userId = AuthHelper.GetUserIdFromRequest(req);
            if (string.IsNullOrEmpty(userId))
            {
                var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorizedResponse.WriteStringAsync("Unauthorized");
                return unauthorizedResponse;
            }

            try
            {
                var applicant = await _context.Applicants
                    .FirstOrDefaultAsync(a => a.UserId == userId);

                if (applicant == null)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteStringAsync("Applicant not found");
                    return notFoundResponse;
                }

                var cv = await _context.CVVersions
                    .FirstOrDefaultAsync(c => c.Id == cvId && c.ApplicantId == applicant.Id);

                if (cv == null)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteStringAsync($"CV with ID {cvId} not found");
                    return notFoundResponse;
                }

                _context.CVVersions.Remove(cv);
                await _context.SaveChangesAsync();

                var response = req.CreateResponse(HttpStatusCode.NoContent);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting CV {cvId}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("An error occurred while deleting the CV");
                return errorResponse;
            }
        }

        [Function("AddReference")]
        public async Task<HttpResponseData> AddReference(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "Applicants/AddReference")] HttpRequestData req)
        {
            _logger.LogInformation("Adding job reference");

            var userId = AuthHelper.GetUserIdFromRequest(req);
            if (string.IsNullOrEmpty(userId))
            {
                var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorizedResponse.WriteStringAsync("Unauthorized");
                return unauthorizedResponse;
            }

            try
            {
                var applicant = await _context.Applicants
                    .FirstOrDefaultAsync(a => a.UserId == userId);

                if (applicant == null)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteStringAsync("Applicant not found");
                    return notFoundResponse;
                }

                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var referenceDto = JsonConvert.DeserializeObject<JobReferenceDto>(requestBody);

                if (referenceDto == null)
                {
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteStringAsync("Invalid reference data");
                    return badRequestResponse;
                }

                var reference = referenceDto.ToEntity(applicant.Id);

                _context.JobReferences.Add(reference);
                await _context.SaveChangesAsync();

                var createdDto = reference.ToDto();

                var response = req.CreateResponse(HttpStatusCode.Created);
                await response.WriteAsJsonAsync(createdDto);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding job reference");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("An error occurred while adding the reference");
                return errorResponse;
            }
        }

        [Function("DeleteReference")]
        public async Task<HttpResponseData> DeleteReference(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "Applicants/DeleteReference/{referenceId}")] HttpRequestData req,
            string referenceId)
        {
            _logger.LogInformation($"Deleting job reference {referenceId}");

            var userId = AuthHelper.GetUserIdFromRequest(req);
            if (string.IsNullOrEmpty(userId))
            {
                var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorizedResponse.WriteStringAsync("Unauthorized");
                return unauthorizedResponse;
            }

            try
            {
                var applicant = await _context.Applicants
                    .FirstOrDefaultAsync(a => a.UserId == userId);

                if (applicant == null)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteStringAsync("Applicant not found");
                    return notFoundResponse;
                }

                var reference = await _context.JobReferences
                    .FirstOrDefaultAsync(r => r.Id == referenceId && r.ApplicantId == applicant.Id);

                if (reference == null)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteStringAsync($"Reference with ID {referenceId} not found");
                    return notFoundResponse;
                }

                _context.JobReferences.Remove(reference);
                await _context.SaveChangesAsync();

                var response = req.CreateResponse(HttpStatusCode.NoContent);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting job reference {referenceId}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("An error occurred while deleting the reference");
                return errorResponse;
            }
        }
    }
}
