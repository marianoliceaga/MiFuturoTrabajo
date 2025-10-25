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
    public class MeetingFunctions
    {
        private readonly ILogger<MeetingFunctions> _logger;
        private readonly ApplicationDbContext _context;

        public MeetingFunctions(ILogger<MeetingFunctions> logger, ApplicationDbContext context)
        {
            _logger = logger;
            _context = context;
        }

        [Function("Meetings")]
        public async Task<HttpResponseData> GetAll([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "Meetings")] HttpRequestData req)
        {
            var userId = AuthHelper.GetUserIdFromRequest(req);
            if (string.IsNullOrEmpty(userId)) return await CreateUnauthorizedResponse(req);

            try
            {
                var applicant = await _context.Applicants.FirstOrDefaultAsync(a => a.UserId == userId);
                if (applicant == null) return await CreateNotFoundResponse(req, "Applicant not found");

                var meetings = await _context.Meetings.Where(m => m.ApplicantId == applicant.Id)
                    .Include(m => m.MeetingRecruiters).ThenInclude(mr => mr.Recruiter).ToListAsync();

                var meetingsDto = meetings.Select(m =>
                {
                    var recruiters = m.MeetingRecruiters.Select(mr => mr.Recruiter).ToList();
                    return m.ToDto(recruiters);
                }).ToList();

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(meetingsDto);
                return response;
            }
            catch (Exception ex)
            {
                return await CreateErrorResponse(req, ex, "Error getting meetings");
            }
        }

        [Function("MeetingGetById")]
        public async Task<HttpResponseData> GetById([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "Meetings/MeetingGetById/{id}")] HttpRequestData req, string id)
        {
            var userId = AuthHelper.GetUserIdFromRequest(req);
            if (string.IsNullOrEmpty(userId)) return await CreateUnauthorizedResponse(req);

            try
            {
                var applicant = await _context.Applicants.FirstOrDefaultAsync(a => a.UserId == userId);
                if (applicant == null) return await CreateNotFoundResponse(req, "Applicant not found");

                var meeting = await _context.Meetings.Include(m => m.MeetingRecruiters).ThenInclude(mr => mr.Recruiter)
                    .FirstOrDefaultAsync(m => m.Id == id && m.ApplicantId == applicant.Id);

                if (meeting == null) return await CreateNotFoundResponse(req, $"Meeting with ID {id} not found");

                var recruiters = meeting.MeetingRecruiters.Select(mr => mr.Recruiter).ToList();
                var meetingDto = meeting.ToDto(recruiters);

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(meetingDto);
                return response;
            }
            catch (Exception ex)
            {
                return await CreateErrorResponse(req, ex, $"Error getting meeting with ID: {id}");
            }
        }

        [Function("MeetingAdd")]
        public async Task<HttpResponseData> Create([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "Meetings/MeetingAdd")] HttpRequestData req)
        {
            var userId = AuthHelper.GetUserIdFromRequest(req);
            if (string.IsNullOrEmpty(userId)) return await CreateUnauthorizedResponse(req);

            try
            {
                var applicant = await _context.Applicants.FirstOrDefaultAsync(a => a.UserId == userId);
                if (applicant == null) return await CreateNotFoundResponse(req, "Applicant not found");

                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var meetingDto = JsonConvert.DeserializeObject<MeetingDto>(requestBody);

                if (meetingDto == null) return await CreateBadRequestResponse(req, "Invalid meeting data");

                var meeting = meetingDto.ToEntity(applicant.Id);
                _context.Meetings.Add(meeting);

                // Add recruiter associations
                if (meetingDto.Recruiters != null)
                {
                    foreach (var recruiterDto in meetingDto.Recruiters)
                    {
                        _context.MeetingRecruiters.Add(new Infrastructure.Entities.MeetingRecruiter
                        {
                            MeetingId = meeting.Id,
                            RecruiterId = recruiterDto.Id
                        });
                    }
                }

                await _context.SaveChangesAsync();

                var createdDto = meeting.ToDto(meetingDto.Recruiters?.Select(r => r.ToEntity(userId)).ToList());

                var response = req.CreateResponse(HttpStatusCode.Created);
                await response.WriteAsJsonAsync(createdDto);
                return response;
            }
            catch (Exception ex)
            {
                return await CreateErrorResponse(req, ex, "Error creating meeting");
            }
        }

        [Function("MeetingUpdate")]
        public async Task<HttpResponseData> Update([HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "Meetings/{id}")] HttpRequestData req, string id)
        {
            var userId = AuthHelper.GetUserIdFromRequest(req);
            if (string.IsNullOrEmpty(userId)) return await CreateUnauthorizedResponse(req);

            try
            {
                var applicant = await _context.Applicants.FirstOrDefaultAsync(a => a.UserId == userId);
                if (applicant == null) return await CreateNotFoundResponse(req, "Applicant not found");

                var meeting = await _context.Meetings.Include(m => m.MeetingRecruiters)
                    .FirstOrDefaultAsync(m => m.Id == id && m.ApplicantId == applicant.Id);

                if (meeting == null) return await CreateNotFoundResponse(req, $"Meeting with ID {id} not found");

                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var meetingDto = JsonConvert.DeserializeObject<MeetingDto>(requestBody);

                if (meetingDto == null) return await CreateBadRequestResponse(req, "Invalid meeting data");

                // Update properties
                meeting.Title = meetingDto.Title ?? meeting.Title;
                meeting.Description = meetingDto.Description ?? meeting.Description;
                meeting.StartDateTime = meetingDto.StartDateTime;
                meeting.EndDateTime = meetingDto.EndDateTime;
                meeting.IsPhonecall = meetingDto.IsPhonecall;
                meeting.OnlineMeetingURL = meetingDto.OnlineMeetingURL ?? meeting.OnlineMeetingURL;
                meeting.PhysicalLocation = meetingDto.PhysicalLocation ?? meeting.PhysicalLocation;
                meeting.MeetingAccessCode = meetingDto.MeetingAccessCode ?? meeting.MeetingAccessCode;
                meeting.Outcome = meetingDto.Outcome ?? meeting.Outcome;
                meeting.Note = meetingDto.Note ?? meeting.Note;
                meeting.SendReminderEmail = meetingDto.SendReminderEmail;
                meeting.ReminderMinutesBefore = meetingDto.ReminderMinutesBefore;

                // Update recruiters
                if (meetingDto.Recruiters != null)
                {
                    _context.MeetingRecruiters.RemoveRange(meeting.MeetingRecruiters);
                    foreach (var recruiterDto in meetingDto.Recruiters)
                    {
                        _context.MeetingRecruiters.Add(new Infrastructure.Entities.MeetingRecruiter
                        {
                            MeetingId = meeting.Id,
                            RecruiterId = recruiterDto.Id
                        });
                    }
                }

                await _context.SaveChangesAsync();

                var updatedDto = meeting.ToDto();

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(updatedDto);
                return response;
            }
            catch (Exception ex)
            {
                return await CreateErrorResponse(req, ex, $"Error updating meeting with ID: {id}");
            }
        }

        [Function("MeetingDeleteById")]
        public async Task<HttpResponseData> Delete([HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "Meetings/MeetingDeleteById/{id}")] HttpRequestData req, string id)
        {
            var userId = AuthHelper.GetUserIdFromRequest(req);
            if (string.IsNullOrEmpty(userId)) return await CreateUnauthorizedResponse(req);

            try
            {
                var applicant = await _context.Applicants.FirstOrDefaultAsync(a => a.UserId == userId);
                if (applicant == null) return await CreateNotFoundResponse(req, "Applicant not found");

                var meeting = await _context.Meetings.FirstOrDefaultAsync(m => m.Id == id && m.ApplicantId == applicant.Id);

                if (meeting == null) return await CreateNotFoundResponse(req, $"Meeting with ID {id} not found");

                _context.Meetings.Remove(meeting);
                await _context.SaveChangesAsync();

                return req.CreateResponse(HttpStatusCode.NoContent);
            }
            catch (Exception ex)
            {
                return await CreateErrorResponse(req, ex, $"Error deleting meeting with ID: {id}");
            }
        }

        // Helper methods
        private async Task<HttpResponseData> CreateUnauthorizedResponse(HttpRequestData req)
        {
            var response = req.CreateResponse(HttpStatusCode.Unauthorized);
            await response.WriteStringAsync("Unauthorized");
            return response;
        }

        private async Task<HttpResponseData> CreateNotFoundResponse(HttpRequestData req, string message)
        {
            var response = req.CreateResponse(HttpStatusCode.NotFound);
            await response.WriteStringAsync(message);
            return response;
        }

        private async Task<HttpResponseData> CreateBadRequestResponse(HttpRequestData req, string message)
        {
            var response = req.CreateResponse(HttpStatusCode.BadRequest);
            await response.WriteStringAsync(message);
            return response;
        }

        private async Task<HttpResponseData> CreateErrorResponse(HttpRequestData req, Exception ex, string message)
        {
            _logger.LogError(ex, message);
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"An error occurred: {message}");
            return response;
        }
    }
}
