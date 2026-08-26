using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiFuturoTrabajo.Infrastructure;
using MiFuturoTrabajo.Infrastructure.Entities;
using MiFuturoTrabajo.Shared.Models;
using MiFuturoTrabajo.Api.Helpers;
using MiFuturoTrabajo.Api.Services;
using Newtonsoft.Json;
using Microsoft.AspNetCore.RateLimiting;

namespace MiFuturoTrabajo.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    [EnableRateLimiting(RateLimiting.PerUserPolicy)]
    public class JobApplicationController : ControllerBase
    {
        private readonly ILogger<JobApplicationController> _logger;
        private readonly ApplicationDbContext _context;
        private readonly QuotaService _quotas;

        public JobApplicationController(ILogger<JobApplicationController> logger, ApplicationDbContext context, QuotaService quotas)
        {
            _logger = logger;
            _context = context;
            _quotas = quotas;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var userId = AuthHelper.GetUserIdFromClaims(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("Unauthorized");
            }

            try
            {
                var applicant = await _context.Applicants.FirstOrDefaultAsync(a => a.UserId == userId);
                if (applicant == null)
                {
                    return NotFound("Applicant not found");
                }

                // Interview steps, labels, notes and the company snapshot are
                // embedded in each document, so one single-partition query
                // returns fully-formed cards.
                var jobApps = await _context.JobApplications
                    .Where(ja => ja.ApplicantId == applicant.Id)
                    .Where(ja => !ja.IsDeleted)
                    .ToListAsync();

                var recruiters = await _context.Recruiters.Where(r => r.UserId == userId).ToListAsync();
                var tasks = await _context.ApplicantTasks.Where(t => t.ApplicantId == applicant.Id).ToListAsync();

                var jobAppsDto = jobApps.Select(ja => ja.ToDto(recruiters, tasks)).ToList();

                return Ok(jobAppsDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting job applications");
                return StatusCode(500, "An error occurred while retrieving job applications");
            }
        }

        [HttpGet("board")]
        public async Task<IActionResult> GetBoard()
        {
            var userId = AuthHelper.GetUserIdFromClaims(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("Unauthorized");
            }

            try
            {
                var applicant = await _context.Applicants.FirstOrDefaultAsync(a => a.UserId == userId);
                if (applicant == null)
                {
                    return NotFound("Applicant not found");
                }

                // The whole board from a single single-partition query.
                var jobApps = await _context.JobApplications
                    .Where(ja => ja.ApplicantId == applicant.Id)
                    .Where(ja => !ja.IsDeleted)
                    .ToListAsync();

                var recruiters = await _context.Recruiters.Where(r => r.UserId == userId).ToListAsync();

                // Group by status
                var boardColumns = Enum.GetValues<JobApplicationStatus>()
                    .Select(status => new BoardColumnDto
                    {
                        Status = status,
                        Title = status.ToString(),
                        Applications = jobApps
                            .Where(ja => ja.Status == status)
                            .Select(ja => ja.ToDto(recruiters))
                            .ToList()
                    })
                    .ToList();

                return Ok(boardColumns);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting board data");
                return StatusCode(500, "An error occurred while retrieving board data");
            }
        }

        [HttpGet("JobApplicationGetById/{id}")]
        public async Task<IActionResult> GetById(string id)
        {
            var userId = AuthHelper.GetUserIdFromClaims(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("Unauthorized");
            }

            try
            {
                var applicant = await _context.Applicants.FirstOrDefaultAsync(a => a.UserId == userId);
                if (applicant == null)
                {
                    return NotFound("Applicant not found");
                }

                var jobApp = await _context.JobApplications
                    .Where(ja => ja.ApplicantId == applicant.Id)
                    .FirstOrDefaultAsync(ja => ja.Id == id && !ja.IsDeleted);

                if (jobApp == null)
                {
                    return NotFound($"Job application with ID {id} not found");
                }

                var recruiters = await _context.Recruiters.Where(r => r.UserId == userId).ToListAsync();
                var tasks = await _context.ApplicantTasks.Where(t => t.ApplicantId == applicant.Id).ToListAsync();

                var jobAppDto = jobApp.ToDto(recruiters, tasks);

                return Ok(jobAppDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting job application with ID: {id}");
                return StatusCode(500, "An error occurred while retrieving the job application");
            }
        }

        [HttpPost("JobApplicationAdd")]
        public async Task<IActionResult> Create([FromBody] JobApplicationDto jobAppDto)
        {
            var userId = AuthHelper.GetUserIdFromClaims(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("Unauthorized");
            }

            try
            {
                var applicant = await _context.Applicants.FirstOrDefaultAsync(a => a.UserId == userId);
                if (applicant == null)
                {
                    return NotFound("Applicant not found");
                }

                if (jobAppDto == null)
                {
                    return BadRequest("Invalid job application data");
                }

                // ToEntity embeds the recruiter ids and interview steps in the
                // document itself: no join table, no separate step container.
                var jobApp = jobAppDto.ToEntity(applicant.Id);
                await SyncCompanySnapshotAsync(jobApp, userId);

                // Registration is open on a shared free-tier database, so one
                // account cannot be allowed to fill it for everybody else.
                var quotaError = await _quotas.CheckJobApplicationsAsync(applicant.Id, HttpContext.RequestAborted);
                if (quotaError != null)
                {
                    return StatusCode(429, new { error = quotaError });
                }

                _context.JobApplications.Add(jobApp);
                await _context.SaveChangesAsync();

                var recruiterPool = await _context.Recruiters.Where(r => r.UserId == userId).ToListAsync();
                var createdDto = jobApp.ToDto(recruiterPool);
                return StatusCode(201, createdDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating job application");
                return StatusCode(500, "An error occurred while creating the job application");
            }
        }

        [HttpPatch("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] JobApplicationDto jobAppDto)
        {
            var userId = AuthHelper.GetUserIdFromClaims(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("Unauthorized");
            }

            try
            {
                var applicant = await _context.Applicants.FirstOrDefaultAsync(a => a.UserId == userId);
                if (applicant == null)
                {
                    return NotFound("Applicant not found");
                }

                var jobApp = await _context.JobApplications
                    .Where(ja => ja.ApplicantId == applicant.Id)
                    .FirstOrDefaultAsync(ja => ja.Id == id && !ja.IsDeleted);

                if (jobApp == null)
                {
                    return NotFound($"Job application with ID {id} not found");
                }

                if (jobAppDto == null)
                {
                    return BadRequest("Invalid job application data");
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
                jobApp.Notes = new List<string>(jobAppDto.Notes);
                jobApp.Labels = new List<string>(jobAppDto.Labels);
                jobApp.DeadLine = jobAppDto.DeadLine;
                jobApp.JobOffer = jobAppDto.JobOffer ?? jobApp.JobOffer;
                jobApp.CVUsedId = jobAppDto.CVUsedId ?? jobApp.CVUsedId;

                // Both collections live on the document; replace them wholesale.
                if (jobAppDto.Recruiters != null)
                {
                    jobApp.RecruiterIds = jobAppDto.Recruiters
                        .Select(r => r.Id)
                        .Where(rid => !string.IsNullOrWhiteSpace(rid))
                        .ToList();
                }

                if (jobAppDto.InterviewSteps != null)
                {
                    jobApp.InterviewSteps = jobAppDto.InterviewSteps
                        .Select(s => s.ToEntity())
                        .ToList();
                }

                await SyncCompanySnapshotAsync(jobApp, userId);
                await _context.SaveChangesAsync();

                var updatedRecruiters = await _context.Recruiters.Where(r => r.UserId == userId).ToListAsync();
                var updatedDto = jobApp.ToDto(updatedRecruiters);
                return Ok(updatedDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating job application with ID: {id}");
                return StatusCode(500, "An error occurred while updating the job application");
            }
        }

        [HttpDelete("JobApplicationDeleteById/{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            var userId = AuthHelper.GetUserIdFromClaims(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("Unauthorized");
            }

            try
            {
                var applicant = await _context.Applicants.FirstOrDefaultAsync(a => a.UserId == userId);
                if (applicant == null)
                {
                    return NotFound("Applicant not found");
                }

                var jobApp = await _context.JobApplications
                    .FirstOrDefaultAsync(ja => ja.Id == id && ja.ApplicantId == applicant.Id && !ja.IsDeleted);

                if (jobApp == null)
                {
                    return NotFound($"Job application with ID {id} not found");
                }

                // Soft delete
                jobApp.IsDeleted = true;
                await _context.SaveChangesAsync();

                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting job application with ID: {id}");
                return StatusCode(500, "An error occurred while deleting the job application");
            }
        }

        [HttpPatch("{id}/status")]
        public async Task<IActionResult> UpdateStatus(string id, [FromBody] Dictionary<string, JobApplicationStatus> statusUpdate)
        {
            var userId = AuthHelper.GetUserIdFromClaims(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("Unauthorized");
            }

            try
            {
                var applicant = await _context.Applicants.FirstOrDefaultAsync(a => a.UserId == userId);
                if (applicant == null)
                {
                    return NotFound("Applicant not found");
                }

                var jobApp = await _context.JobApplications
                    .FirstOrDefaultAsync(ja => ja.Id == id && ja.ApplicantId == applicant.Id && !ja.IsDeleted);

                if (jobApp == null)
                {
                    return NotFound($"Job application with ID {id} not found");
                }

                if (statusUpdate == null || !statusUpdate.ContainsKey("status"))
                {
                    return BadRequest("Invalid status data");
                }

                jobApp.Status = statusUpdate["status"];
                await _context.SaveChangesAsync();

                var statusRecruiters = await _context.Recruiters.Where(r => r.UserId == userId).ToListAsync();
                var updatedDto = jobApp.ToDto(statusRecruiters);
                return Ok(updatedDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating status for job application with ID: {id}");
                return StatusCode(500, "An error occurred while updating the job application status");
            }
        }

        /// <summary>
        /// Keeps the embedded CompanySnapshot in step with the Companies
        /// container.
        ///
        /// The company name is denormalised into the application document so
        /// the board can render from one single-partition query rather than a
        /// cross-container join, which Cosmos cannot perform. That trade buys
        /// read speed at the cost of this write-time sync.
        /// </summary>
        private async Task SyncCompanySnapshotAsync(JobApplication jobApp, string userId)
        {
            if (string.IsNullOrWhiteSpace(jobApp.CompanyId))
            {
                jobApp.Company = null;
                return;
            }

            var company = await _context.Companies
                .Where(c => c.UserId == userId)
                .FirstOrDefaultAsync(c => c.Id == jobApp.CompanyId);

            if (company == null)
            {
                // Unknown company: keep the id so the link is not lost, but do
                // not invent a name.
                jobApp.Company = new CompanySnapshot { Id = jobApp.CompanyId };
                return;
            }

            jobApp.Company = new CompanySnapshot { Id = company.Id, Name = company.Name };
        }
    }
}
