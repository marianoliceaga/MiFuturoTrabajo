using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Net;
using Letmework4U.Infrastructure;
using Letmework4U.Shared.Models;
using Letmework4U.Functions.Helpers;
using Letmework4U.Functions.Services;
using Newtonsoft.Json;

namespace Letmework4U.Functions.Functions
{
    public class ApplicantTaskFunctions
    {
        private readonly ILogger<ApplicantTaskFunctions> _logger;
        private readonly ApplicationDbContext _context;

        public ApplicantTaskFunctions(ILogger<ApplicantTaskFunctions> logger, ApplicationDbContext context)
        {
            _logger = logger;
            _context = context;
        }

        [Function("ApplicantTasks")]
        public async Task<HttpResponseData> GetAll(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ApplicantTasks")] HttpRequestData req)
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

                var tasks = await _context.ApplicantTasks
                    .Where(t => t.ApplicantId == applicant.Id)
                    .ToListAsync();

                var tasksDto = tasks.Select(t => t.ToDto()).ToList();

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(tasksDto);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting applicant tasks");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("An error occurred while retrieving tasks");
                return errorResponse;
            }
        }

        [Function("ApplicantTaskGetById")]
        public async Task<HttpResponseData> GetById(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ApplicantTasks/ApplicantTaskGetById/{id}")] HttpRequestData req,
            string id)
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

                var task = await _context.ApplicantTasks
                    .FirstOrDefaultAsync(t => t.Id == id && t.ApplicantId == applicant.Id);

                if (task == null)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteStringAsync($"Task with ID {id} not found");
                    return notFoundResponse;
                }

                var taskDto = task.ToDto();

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(taskDto);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting task with ID: {id}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("An error occurred while retrieving the task");
                return errorResponse;
            }
        }

        [Function("ApplicantTaskAdd")]
        public async Task<HttpResponseData> Create(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ApplicantTasks/ApplicantTaskAdd")] HttpRequestData req)
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
                var taskDto = JsonConvert.DeserializeObject<ApplicantTaskDto>(requestBody);

                if (taskDto == null)
                {
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteStringAsync("Invalid task data");
                    return badRequestResponse;
                }

                var task = taskDto.ToEntity(applicant.Id);

                _context.ApplicantTasks.Add(task);
                await _context.SaveChangesAsync();

                var createdDto = task.ToDto();

                var response = req.CreateResponse(HttpStatusCode.Created);
                await response.WriteAsJsonAsync(createdDto);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating task");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("An error occurred while creating the task");
                return errorResponse;
            }
        }

        [Function("ApplicantTaskUpdate")]
        public async Task<HttpResponseData> Update(
            [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "ApplicantTasks/{id}")] HttpRequestData req,
            string id)
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

                var task = await _context.ApplicantTasks
                    .FirstOrDefaultAsync(t => t.Id == id && t.ApplicantId == applicant.Id);

                if (task == null)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteStringAsync($"Task with ID {id} not found");
                    return notFoundResponse;
                }

                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var taskDto = JsonConvert.DeserializeObject<ApplicantTaskDto>(requestBody);

                if (taskDto == null)
                {
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteStringAsync("Invalid task data");
                    return badRequestResponse;
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

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(updatedDto);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating task with ID: {id}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("An error occurred while updating the task");
                return errorResponse;
            }
        }

        [Function("ApplicantTaskDeleteById")]
        public async Task<HttpResponseData> Delete(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "ApplicantTasks/ApplicantTaskDeleteById/{id}")] HttpRequestData req,
            string id)
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

                var task = await _context.ApplicantTasks
                    .FirstOrDefaultAsync(t => t.Id == id && t.ApplicantId == applicant.Id);

                if (task == null)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteStringAsync($"Task with ID {id} not found");
                    return notFoundResponse;
                }

                _context.ApplicantTasks.Remove(task);
                await _context.SaveChangesAsync();

                var response = req.CreateResponse(HttpStatusCode.NoContent);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting task with ID: {id}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("An error occurred while deleting the task");
                return errorResponse;
            }
        }
    }
}
