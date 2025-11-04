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
    public class ApplicantTaskController : ControllerBase
    {
        private readonly ILogger<ApplicantTaskController> _logger;
        private readonly ApplicationDbContext _context;

        public ApplicantTaskController(ILogger<ApplicantTaskController> logger, ApplicationDbContext context)
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

                var tasks = await _context.ApplicantTasks
                    .Where(t => t.ApplicantId == applicant.Id)
                    .ToListAsync();

                var tasksDto = tasks.Select(t => t.ToDto()).ToList();

                return Ok(tasksDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting applicant tasks");
                return StatusCode(500, "An error occurred while retrieving tasks");
            }
        }

        [HttpGet("ApplicantTaskGetById/{id}")]
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

                var task = await _context.ApplicantTasks
                    .FirstOrDefaultAsync(t => t.Id == id && t.ApplicantId == applicant.Id);

                if (task == null)
                {
                    return NotFound($"Task with ID {id} not found");
                }

                var taskDto = task.ToDto();

                return Ok(taskDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting task with ID: {id}");
                return StatusCode(500, "An error occurred while retrieving the task");
            }
        }

        [HttpPost("ApplicantTaskAdd")]
        public async Task<IActionResult> Create([FromBody] ApplicantTaskDto taskDto)
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

                if (taskDto == null)
                {
                    return BadRequest("Invalid task data");
                }

                var task = taskDto.ToEntity(applicant.Id);

                _context.ApplicantTasks.Add(task);
                await _context.SaveChangesAsync();

                var createdDto = task.ToDto();

                return StatusCode(201, createdDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating task");
                return StatusCode(500, "An error occurred while creating the task");
            }
        }

        [HttpPatch("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] ApplicantTaskDto taskDto)
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

                var task = await _context.ApplicantTasks
                    .FirstOrDefaultAsync(t => t.Id == id && t.ApplicantId == applicant.Id);

                if (task == null)
                {
                    return NotFound($"Task with ID {id} not found");
                }

                if (taskDto == null)
                {
                    return BadRequest("Invalid task data");
                }

                // Update properties
                task.Type = taskDto.Type;
                task.Title = taskDto.Title ?? task.Title;
                task.Note = taskDto.Note ?? task.Note;
                task.StartDate = taskDto.StartDate;
                task.Deadline = taskDto.Deadline;
                task.Completed = taskDto.Completed;
                task.JobApplicationId = taskDto.JobApplicationId ?? task.JobApplicationId;

                await _context.SaveChangesAsync();

                var updatedDto = task.ToDto();

                return Ok(updatedDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating task with ID: {id}");
                return StatusCode(500, "An error occurred while updating the task");
            }
        }

        [HttpDelete("ApplicantTaskDeleteById/{id}")]
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

                var task = await _context.ApplicantTasks
                    .FirstOrDefaultAsync(t => t.Id == id && t.ApplicantId == applicant.Id);

                if (task == null)
                {
                    return NotFound($"Task with ID {id} not found");
                }

                _context.ApplicantTasks.Remove(task);
                await _context.SaveChangesAsync();

                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting task with ID: {id}");
                return StatusCode(500, "An error occurred while deleting the task");
            }
        }
    }
}
