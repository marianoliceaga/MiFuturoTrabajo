using MiFuturoTrabajo.Shared.Models;

namespace MiFuturoTrabajo.Client.Services
{
    public interface IJobApplicationService
    {
        Task<List<JobApplicationDto>> GetAllAsync();
        Task<JobApplicationDto> GetByIdAsync(string id);
        Task<JobApplicationDto> CreateAsync(JobApplicationDto application);
        Task<JobApplicationDto> UpdateAsync(string id, JobApplicationDto application);
        Task DeleteAsync(string id);
        Task<List<BoardColumnDto>> GetBoardDataAsync();
        Task<JobApplicationDto> UpdateStatusAsync(string id, JobApplicationStatus newStatus);
    }

    public interface IApplicantService
    {
        Task<ApplicantDto> GetCurrentApplicantAsync();
        Task<ApplicantDto> UpdateProfileAsync(ApplicantDto applicant);
        Task<CVVersionDto> UploadCVAsync(string name, string fileName, string contentType, Stream fileStream);
        Task<CVDownloadLinkDto> GetCVDownloadLinkAsync(string cvId);
        Task<JobReferenceDto> AddReferenceAsync(JobReferenceDto reference);
        Task DeleteReferenceAsync(string referenceId);
    }

    public interface IMeetingService
    {
        Task<List<MeetingDto>> GetAllAsync();
        Task<List<MeetingDto>> GetByApplicantAsync(string applicantId);
        Task<MeetingDto> GetByIdAsync(string id);
        Task<MeetingDto> CreateAsync(MeetingDto meeting);
        Task<MeetingDto> UpdateAsync(string id, MeetingDto meeting);
        Task DeleteAsync(string id);
        Task<List<MeetingDto>> GetUpcomingAsync();
    }

    public interface IApplicantTaskService
    {
        Task<List<ApplicantTaskDto>> GetAllAsync();
        Task<List<ApplicantTaskDto>> GetByApplicantAsync(string applicantId);
        Task<ApplicantTaskDto> GetByIdAsync(string id);
        Task<ApplicantTaskDto> CreateAsync(ApplicantTaskDto task);
        Task<ApplicantTaskDto> UpdateAsync(string id, ApplicantTaskDto task);
        Task DeleteAsync(string id);
        Task<ApplicantTaskDto> ToggleCompleteAsync(string id);
        Task<List<ApplicantTaskDto>> GetUpcomingAsync();
    }

    public interface ICompanyService
    {
        Task<List<CompanyDto>> GetAllAsync();
        Task<CompanyDto> GetByIdAsync(string id);
        Task<CompanyDto> CreateAsync(CompanyDto company);
        Task<CompanyDto> UpdateAsync(string id, CompanyDto company);
        Task DeleteAsync(string id);
        Task<List<CompanyDto>> SearchAsync(string searchTerm);
    }

    public interface IRecruiterService
    {
        Task<List<RecruiterDto>> GetAllAsync();
        Task<RecruiterDto> GetByIdAsync(string id);
        Task<RecruiterDto> CreateAsync(RecruiterDto recruiter);
        Task<RecruiterDto> UpdateAsync(string id, RecruiterDto recruiter);
        Task DeleteAsync(string id);
    }

    public interface ICVVersionService
    {
        Task<List<CVVersionDto>> GetAllAsync();
        Task<CVVersionDto> GetByIdAsync(string id);
        Task<CVVersionDto> CreateAsync(CVVersionDto cvVersion);
        Task<CVVersionDto> UpdateAsync(string id, CVVersionDto cvVersion);
        Task<CVVersionDto> SetDefaultAsync(string id);
        Task DeleteAsync(string id);
        Task<CVVersionDto> UploadAsync(string name, string fileName, string contentType, Stream fileStream);

        /// <summary>
        /// Asks the API for a short-lived link to the stored document. The
        /// client never holds a durable URL, so nothing useful leaks if one is
        /// copied out of browser history.
        /// </summary>
        Task<CVDownloadLinkDto> GetDownloadLinkAsync(string id);
    }

    public interface IAuthService
    {
        Task<ApplicantDto> LoginAsync(string email, string password);
        Task LogoutAsync();
        Task<bool> IsAuthenticatedAsync();
        Task<string> GetCurrentApplicantIdAsync();
    }

    public class StateContainer
    {
        private ApplicantDto _currentApplicant;

        public ApplicantDto CurrentApplicant
        {
            get => _currentApplicant;
            set
            {
                _currentApplicant = value;
                NotifyStateChanged();
            }
        }

        public event Action OnChange;

        private void NotifyStateChanged() => OnChange?.Invoke();
    }

    public interface ICalendarService
    {
        Task<List<CalendarEvent>> GetEventsAsync();
    }
}