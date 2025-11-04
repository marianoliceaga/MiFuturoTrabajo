using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Letmework4U.Infrastructure;
using Letmework4U.Infrastructure.Entities;
using Letmework4U.Shared.Models;
using Letmework4U.Api.Helpers;
using Letmework4U.Api.Services;
using Newtonsoft.Json;

namespace Letmework4U.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class MeetingController : ControllerBase
    {
        private readonly ILogger<MeetingController> _logger;
        private readonly ApplicationDbContext _context;

        public MeetingController(ILogger<MeetingController> logger, ApplicationDbContext context)
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

                var meetings = await _context.Meetings
                    .Where(m => m.ApplicantId == applicant.Id)
                    .Include(m => m.MeetingRecruiters).ThenInclude(mr => mr.Recruiter)
                    .ToListAsync();

                var meetingsDto = meetings.Select(m =>
                {
                    var recruiters = m.MeetingRecruiters.Select(mr => mr.Recruiter).ToList();
                    return m.ToDto(recruiters);
                }).ToList();

                return Ok(meetingsDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting meetings");
                return StatusCode(500, "An error occurred while retrieving meetings");
            }
        }

        [HttpGet("MeetingGetById/{id}")]
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

                var meeting = await _context.Meetings
                    .Include(m => m.MeetingRecruiters).ThenInclude(mr => mr.Recruiter)
                    .FirstOrDefaultAsync(m => m.Id == id && m.ApplicantId == applicant.Id);

                if (meeting == null)
                {
                    return NotFound($"Meeting with ID {id} not found");
                }

                var recruiters = meeting.MeetingRecruiters.Select(mr => mr.Recruiter).ToList();
                var meetingDto = meeting.ToDto(recruiters);

                return Ok(meetingDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting meeting with ID: {id}");
                return StatusCode(500, "An error occurred while retrieving the meeting");
            }
        }

        [HttpPost("MeetingAdd")]
        public async Task<IActionResult> Create([FromBody] MeetingDto meetingDto)
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

                if (meetingDto == null)
                {
                    return BadRequest("Invalid meeting data");
                }

                var meeting = meetingDto.ToEntity(applicant.Id);
                _context.Meetings.Add(meeting);

                // Add recruiter associations
                if (meetingDto.Recruiters != null)
                {
                    foreach (var recruiterDto in meetingDto.Recruiters)
                    {
                        _context.MeetingRecruiters.Add(new MeetingRecruiter
                        {
                            MeetingId = meeting.Id,
                            RecruiterId = recruiterDto.Id
                        });
                    }
                }

                await _context.SaveChangesAsync();

                var createdDto = meeting.ToDto(meetingDto.Recruiters?.Select(r => r.ToEntity(userId)).ToList());

                return StatusCode(201, createdDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating meeting");
                return StatusCode(500, "An error occurred while creating the meeting");
            }
        }

        [HttpPatch("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] MeetingDto meetingDto)
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

                var meeting = await _context.Meetings
                    .Include(m => m.MeetingRecruiters)
                    .FirstOrDefaultAsync(m => m.Id == id && m.ApplicantId == applicant.Id);

                if (meeting == null)
                {
                    return NotFound($"Meeting with ID {id} not found");
                }

                if (meetingDto == null)
                {
                    return BadRequest("Invalid meeting data");
                }

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
                        _context.MeetingRecruiters.Add(new MeetingRecruiter
                        {
                            MeetingId = meeting.Id,
                            RecruiterId = recruiterDto.Id
                        });
                    }
                }

                await _context.SaveChangesAsync();

                var updatedDto = meeting.ToDto();

                return Ok(updatedDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating meeting with ID: {id}");
                return StatusCode(500, "An error occurred while updating the meeting");
            }
        }

        [HttpDelete("MeetingDeleteById/{id}")]
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

                var meeting = await _context.Meetings
                    .FirstOrDefaultAsync(m => m.Id == id && m.ApplicantId == applicant.Id);

                if (meeting == null)
                {
                    return NotFound($"Meeting with ID {id} not found");
                }

                _context.Meetings.Remove(meeting);
                await _context.SaveChangesAsync();

                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting meeting with ID: {id}");
                return StatusCode(500, "An error occurred while deleting the meeting");
            }
        }
    }
}
