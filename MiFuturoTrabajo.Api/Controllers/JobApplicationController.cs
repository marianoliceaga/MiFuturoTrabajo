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
    public class JobApplicationController : ControllerBase
    {
        private readonly ILogger<JobApplicationController> _logger;
        private readonly ApplicationDbContext _context;

        public JobApplicationController(ILogger<JobApplicationController> logger, ApplicationDbContext context)
        {
            _logger = logger;
            _context = context;
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
                    .Include(ja => ja.Company)
                    .Include(ja => ja.JobApplicationRecruiters).ThenInclude(jar => jar.Recruiter)
                    .Include(ja => ja.InterviewSteps)
                    .Include(ja => ja.ApplicantTasks)
                    .FirstOrDefaultAsync(ja => ja.Id == id && ja.ApplicantId == applicant.Id);

                if (jobApp == null)
                {
                    return NotFound($"Job application with ID {id} not found");
                }

                var recruiters = jobApp.JobApplicationRecruiters.Select(jar => jar.Recruiter).ToList();
                var jobAppDto = jobApp.ToDto(jobApp.Company, recruiters);

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
                    .Include(ja => ja.JobApplicationRecruiters)
                    .Include(ja => ja.InterviewSteps)
                    .FirstOrDefaultAsync(ja => ja.Id == id && ja.ApplicantId == applicant.Id);

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
                    .FirstOrDefaultAsync(ja => ja.Id == id && ja.ApplicantId == applicant.Id);

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
                    .FirstOrDefaultAsync(ja => ja.Id == id && ja.ApplicantId == applicant.Id);

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

                var updatedDto = jobApp.ToDto();
                return Ok(updatedDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating status for job application with ID: {id}");
                return StatusCode(500, "An error occurred while updating the job application status");
            }
        }
    }
}
