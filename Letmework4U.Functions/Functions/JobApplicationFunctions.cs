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
    public class JobApplicationFunctions
    {
        private readonly ILogger<JobApplicationFunctions> _logger;
        private readonly ApplicationDbContext _context;

        public JobApplicationFunctions(ILogger<JobApplicationFunctions> logger, ApplicationDbContext context)
        {
            _logger = logger;
            _context = context;
        }

        [Function("JobApplications")]
        public async Task<HttpResponseData> GetAll([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "JobApplications")] HttpRequestData req)
        {
            var userId = AuthHelper.GetUserIdFromRequest(req);
            if (string.IsNullOrEmpty(userId))
            {
                var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorizedResponse.WriteStringAsync("Unauthorized");
                return unauthorizedResponse;
            }

            try
            {
                var applicant = await _context.Applicants.FirstOrDefaultAsync(a => a.UserId == userId);
                if (applicant == null)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteStringAsync("Applicant not found");
                    return notFoundResponse;
                }

                var jobApps = await _context.JobApplications
                    .Where(ja => ja.ApplicantId == applicant.Id && !ja.IsDeleted)
                    .Include(ja => ja.Company)
                    .Include(ja => ja.JobApplicationRecruiters).ThenInclude(jar => jar.Recruiter)
                    .Include(ja => ja.InterviewSteps)
                    .Include(ja => ja.ApplicantTasks)
                    .ToListAsync();

                var jobAppsDto = jobApps.Select(ja =>
                {
                    var recruiters = ja.JobApplicationRecruiters.Select(jar => jar.Recruiter).ToList();
                    return ja.ToDto(ja.Company, recruiters);
                }).ToList();

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(jobAppsDto);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting job applications");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("An error occurred while retrieving job applications");
                return errorResponse;
            }
        }

        [Function("JobApplicationBoard")]
        public async Task<HttpResponseData> GetBoard([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "JobApplications/board")] HttpRequestData req)
        {
            var userId = AuthHelper.GetUserIdFromRequest(req);
            if (string.IsNullOrEmpty(userId))
            {
                var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorizedResponse.WriteStringAsync("Unauthorized");
                return unauthorizedResponse;
            }

            try
            {
                var applicant = await _context.Applicants.FirstOrDefaultAsync(a => a.UserId == userId);
                if (applicant == null)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteStringAsync("Applicant not found");
                    return notFoundResponse;
                }

                var jobApps = await _context.JobApplications
                    .Where(ja => ja.ApplicantId == applicant.Id && !ja.IsDeleted)
                    .Include(ja => ja.Company)
                    .Include(ja => ja.JobApplicationRecruiters).ThenInclude(jar => jar.Recruiter)
                    .Include(ja => ja.InterviewSteps)
                    .ToListAsync();

                // Group by status
                var boardColumns = Enum.GetValues<JobApplicationStatus>()
                    .Select(status => new BoardColumnDto
                    {
                        Status = status,
                        Title = status.ToString(),
                        Applications = jobApps
                            .Where(ja => ja.Status == status)
                            .Select(ja =>
                            {
                                var recruiters = ja.JobApplicationRecruiters.Select(jar => jar.Recruiter).ToList();
                                return ja.ToDto(ja.Company, recruiters);
                            })
                            .ToList()
                    })
                    .ToList();

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(boardColumns);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting board data");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("An error occurred while retrieving board data");
                return errorResponse;
            }
        }

        [Function("JobApplicationGetById")]
        public async Task<HttpResponseData> GetById([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "JobApplications/JobApplicationGetById/{id}")] HttpRequestData req, string id)
        {
            var userId = AuthHelper.GetUserIdFromRequest(req);
            if (string.IsNullOrEmpty(userId))
            {
                var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorizedResponse.WriteStringAsync("Unauthorized");
                return unauthorizedResponse;
            }

            try
            {
                var applicant = await _context.Applicants.FirstOrDefaultAsync(a => a.UserId == userId);
                if (applicant == null)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteStringAsync("Applicant not found");
                    return notFoundResponse;
                }

                var jobApp = await _context.JobApplications
                    .Include(ja => ja.Company)
                    .Include(ja => ja.JobApplicationRecruiters).ThenInclude(jar => jar.Recruiter)
                    .Include(ja => ja.InterviewSteps)
                    .Include(ja => ja.ApplicantTasks)
                    .FirstOrDefaultAsync(ja => ja.Id == id && ja.ApplicantId == applicant.Id);

                if (jobApp == null)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteStringAsync($"Job application with ID {id} not found");
                    return notFoundResponse;
                }

                var recruiters = jobApp.JobApplicationRecruiters.Select(jar => jar.Recruiter).ToList();
                var jobAppDto = jobApp.ToDto(jobApp.Company, recruiters);

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(jobAppDto);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting job application with ID: {id}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("An error occurred while retrieving the job application");
                return errorResponse;
            }
        }

        [Function("JobApplicationAdd")]
        public async Task<HttpResponseData> Create([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "JobApplications/JobApplicationAdd")] HttpRequestData req)
        {
            var userId = AuthHelper.GetUserIdFromRequest(req);
            if (string.IsNullOrEmpty(userId))
            {
                var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorizedResponse.WriteStringAsync("Unauthorized");
                return unauthorizedResponse;
            }

            try
            {
                var applicant = await _context.Applicants.FirstOrDefaultAsync(a => a.UserId == userId);
                if (applicant == null)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteStringAsync("Applicant not found");
                    return notFoundResponse;
                }

                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var jobAppDto = JsonConvert.DeserializeObject<JobApplicationDto>(requestBody);

                if (jobAppDto == null)
                {
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteStringAsync("Invalid job application data");
                    return badRequestResponse;
                }

                var jobApp = jobAppDto.ToEntity(applicant.Id);
                _context.JobApplications.Add(jobApp);

                // Add recruiter associations
                if (jobAppDto.Recruiters != null)
                {
                    foreach (var recruiterDto in jobAppDto.Recruiters)
                    {
                        _context.JobApplicationRecruiters.Add(new JobApplicationRecruiter
                        {
                            JobApplicationId = jobApp.Id,
                            RecruiterId = recruiterDto.Id
                        });
                    }
                }

                // Add interview steps
                if (jobAppDto.InterviewSteps != null)
                {
                    foreach (var stepDto in jobAppDto.InterviewSteps)
                    {
                        var step = stepDto.ToEntity(jobApp.Id);
                        _context.InterviewSteps.Add(step);
                    }
                }

                await _context.SaveChangesAsync();

                var createdDto = jobApp.ToDto();
                var response = req.CreateResponse(HttpStatusCode.Created);
                await response.WriteAsJsonAsync(createdDto);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating job application");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("An error occurred while creating the job application");
                return errorResponse;
            }
        }

        [Function("JobApplicationUpdate")]
        public async Task<HttpResponseData> Update([HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "JobApplications/{id}")] HttpRequestData req, string id)
        {
            var userId = AuthHelper.GetUserIdFromRequest(req);
            if (string.IsNullOrEmpty(userId))
            {
                var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorizedResponse.WriteStringAsync("Unauthorized");
                return unauthorizedResponse;
            }

            try
            {
                var applicant = await _context.Applicants.FirstOrDefaultAsync(a => a.UserId == userId);
                if (applicant == null)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteStringAsync("Applicant not found");
                    return notFoundResponse;
                }

                var jobApp = await _context.JobApplications
                    .Include(ja => ja.JobApplicationRecruiters)
                    .Include(ja => ja.InterviewSteps)
                    .FirstOrDefaultAsync(ja => ja.Id == id && ja.ApplicantId == applicant.Id);

                if (jobApp == null)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteStringAsync($"Job application with ID {id} not found");
                    return notFoundResponse;
                }

                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var jobAppDto = JsonConvert.DeserializeObject<JobApplicationDto>(requestBody);

                if (jobAppDto == null)
                {
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteStringAsync("Invalid job application data");
                    return badRequestResponse;
                }

                // Update properties
                jobApp.JobTitle = jobAppDto.JobTitle ?? jobApp.JobTitle;
                jobApp.CompanyId = jobAppDto.Company?.Id ?? jobApp.CompanyId;
                jobApp.Source = jobAppDto.Source;
                jobApp.URL = jobAppDto.URL ?? jobApp.URL;
                jobApp.JobDescription = jobAppDto.JobDescription ?? jobApp.JobDescription;
                jobApp.Salary = jobAppDto.Salary;
                jobApp.Location = jobAppDto.Location ?? jobApp.Location;
                jobApp.IsDirect = jobAppDto.IsDirect;
                jobApp.IsConsultancy = jobAppDto.IsConsultancy;
                jobApp.Status = jobAppDto.Status;
                jobApp.DateApplied = jobAppDto.DateApplied;
                jobApp.MainRecruiterContactId = jobAppDto.MainRecruiterContactId ?? jobApp.MainRecruiterContactId;
                jobApp.NotesJson = JsonConvert.SerializeObject(jobAppDto.Notes ?? new List<string>());
                jobApp.LabelsJson = JsonConvert.SerializeObject(jobAppDto.Labels ?? new List<string>());
                jobApp.DeadLine = jobAppDto.DeadLine;
                jobApp.JobOffer = jobAppDto.JobOffer ?? jobApp.JobOffer;
                jobApp.CVUsedId = jobAppDto.CVUsedId ?? jobApp.CVUsedId;

                // Update recruiters
                if (jobAppDto.Recruiters != null)
                {
                    _context.JobApplicationRecruiters.RemoveRange(jobApp.JobApplicationRecruiters);
                    foreach (var recruiterDto in jobAppDto.Recruiters)
                    {
                        _context.JobApplicationRecruiters.Add(new JobApplicationRecruiter
                        {
                            JobApplicationId = jobApp.Id,
                            RecruiterId = recruiterDto.Id
                        });
                    }
                }

                // Update interview steps
                if (jobAppDto.InterviewSteps != null)
                {
                    _context.InterviewSteps.RemoveRange(jobApp.InterviewSteps);
                    foreach (var stepDto in jobAppDto.InterviewSteps)
                    {
                        var step = stepDto.ToEntity(jobApp.Id);
                        _context.InterviewSteps.Add(step);
                    }
                }

                await _context.SaveChangesAsync();

                var updatedDto = jobApp.ToDto();
                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(updatedDto);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating job application with ID: {id}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("An error occurred while updating the job application");
                return errorResponse;
            }
        }

        [Function("JobApplicationDeleteById")]
        public async Task<HttpResponseData> Delete([HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "JobApplications/JobApplicationDeleteById/{id}")] HttpRequestData req, string id)
        {
            var userId = AuthHelper.GetUserIdFromRequest(req);
            if (string.IsNullOrEmpty(userId))
            {
                var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorizedResponse.WriteStringAsync("Unauthorized");
                return unauthorizedResponse;
            }

            try
            {
                var applicant = await _context.Applicants.FirstOrDefaultAsync(a => a.UserId == userId);
                if (applicant == null)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteStringAsync("Applicant not found");
                    return notFoundResponse;
                }

                var jobApp = await _context.JobApplications
                    .FirstOrDefaultAsync(ja => ja.Id == id && ja.ApplicantId == applicant.Id);

                if (jobApp == null)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteStringAsync($"Job application with ID {id} not found");
                    return notFoundResponse;
                }

                // Soft delete
                jobApp.IsDeleted = true;
                await _context.SaveChangesAsync();

                var response = req.CreateResponse(HttpStatusCode.NoContent);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting job application with ID: {id}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("An error occurred while deleting the job application");
                return errorResponse;
            }
        }

        [Function("JobApplicationUpdateStatus")]
        public async Task<HttpResponseData> UpdateStatus([HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "JobApplications/{id}/status")] HttpRequestData req, string id)
        {
            var userId = AuthHelper.GetUserIdFromRequest(req);
            if (string.IsNullOrEmpty(userId))
            {
                var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorizedResponse.WriteStringAsync("Unauthorized");
                return unauthorizedResponse;
            }

            try
            {
                var applicant = await _context.Applicants.FirstOrDefaultAsync(a => a.UserId == userId);
                if (applicant == null)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteStringAsync("Applicant not found");
                    return notFoundResponse;
                }

                var jobApp = await _context.JobApplications
                    .FirstOrDefaultAsync(ja => ja.Id == id && ja.ApplicantId == applicant.Id);

                if (jobApp == null)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteStringAsync($"Job application with ID {id} not found");
                    return notFoundResponse;
                }

                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var statusUpdate = JsonConvert.DeserializeObject<Dictionary<string, JobApplicationStatus>>(requestBody);

                if (statusUpdate == null || !statusUpdate.ContainsKey("status"))
                {
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteStringAsync("Invalid status data");
                    return badRequestResponse;
                }

                jobApp.Status = statusUpdate["status"];
                await _context.SaveChangesAsync();

                var updatedDto = jobApp.ToDto();
                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(updatedDto);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating status for job application with ID: {id}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("An error occurred while updating the job application status");
                return errorResponse;
            }
        }
    }
}
