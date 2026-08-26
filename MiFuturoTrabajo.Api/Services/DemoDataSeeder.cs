using Microsoft.EntityFrameworkCore;
using MiFuturoTrabajo.Infrastructure;
using MiFuturoTrabajo.Infrastructure.Entities;
using MiFuturoTrabajo.Shared.Models;

namespace MiFuturoTrabajo.Api.Services
{
    /// <summary>
    /// Creates the demo account, once.
    ///
    /// This is the single most-seen code path in the whole project: a reviewer
    /// clicks the demo link, logs in, and forms an opinion in about ninety
    /// seconds. An empty board says the app does not work even when it does, so
    /// the data here is a plausible three-month job hunt rather than
    /// "Company 1 / Company 2".
    ///
    /// Everything is dated relative to now, so the demo never looks abandoned:
    /// there are always interviews next week and tasks due tomorrow.
    ///
    /// Idempotent by design - it runs on every start of every instance, and
    /// bails the moment it sees the account already exists.
    /// </summary>
    public class DemoDataSeeder
    {
        private readonly ApplicationDbContext _context;
        private readonly IPasswordHasher _passwordHasher;
        private readonly IConfiguration _configuration;
        private readonly ILogger<DemoDataSeeder> _logger;

        public DemoDataSeeder(
            ApplicationDbContext context,
            IPasswordHasher passwordHasher,
            IConfiguration configuration,
            ILogger<DemoDataSeeder> logger)
        {
            _context = context;
            _passwordHasher = passwordHasher;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task SeedAsync(CancellationToken ct = default)
        {
            var email = (_configuration["Demo:Email"] ?? "demo@mifuturotrabajo.app").Trim().ToLowerInvariant();
            var password = _configuration["Demo:Password"];

            if (string.IsNullOrWhiteSpace(password))
            {
                _logger.LogWarning("Demo:Enabled is true but Demo:Password is not set; skipping demo seed.");
                return;
            }

            var existing = await _context.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

            if (existing != null)
            {
                _logger.LogInformation("Demo account already present; nothing to seed.");
                return;
            }

            var user = new User
            {
                Email = email,
                PasswordHash = _passwordHasher.HashPassword(password),
                FirstName = "Alex",
                LastName = "Demo",
                EmailConfirmed = true,
                IsActive = true,
                CreatedAt = DateTime.UtcNow.AddMonths(-3)
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync(ct);

            var applicant = new Applicant
            {
                UserId = user.Id,
                Name = "Alex",
                Lastname = "Demo",
                ContactEmail = email,
                CurrentJob = "Backend Engineer",
                LinkedInUrl = "https://www.linkedin.com/in/example",
                Cellphone = "+34 600 000 000",
                CreatedOnDate = DateTime.UtcNow.AddMonths(-3),
                CVVersions = new List<CVVersion>
                {
                    new()
                    {
                        Name = "Backend engineer CV",
                        UploadedDate = DateTime.UtcNow.AddMonths(-3),
                        IsDefault = true,
                        VersionNotes = "General purpose, .NET focused"
                    },
                    new()
                    {
                        Name = "Platform / SRE CV",
                        UploadedDate = DateTime.UtcNow.AddMonths(-1),
                        VersionNotes = "Leads with infrastructure work"
                    }
                },
                PastJobReferences = new List<JobReference>
                {
                    new()
                    {
                        CompanyName = "Northwind Software",
                        JobTitle = "Backend Engineer",
                        ContactName = "Marta Ruiz",
                        ContactEmail = "marta.ruiz@example.com",
                        StartDate = DateTime.UtcNow.AddYears(-4),
                        EndDate = DateTime.UtcNow.AddYears(-1)
                    }
                }
            };

            _context.Applicants.Add(applicant);
            await _context.SaveChangesAsync(ct);

            user.ApplicantId = applicant.Id;
            _context.Users.Update(user);
            await _context.SaveChangesAsync(ct);

            var companies = Companies(user.Id);
            _context.Companies.AddRange(companies);

            var recruiters = Recruiters(user.Id, companies);
            _context.Recruiters.AddRange(recruiters);

            await _context.SaveChangesAsync(ct);

            var applications = Applications(applicant.Id, companies, recruiters);
            _context.JobApplications.AddRange(applications);

            _context.Meetings.AddRange(Meetings(applicant.Id, applications));
            _context.ApplicantTasks.AddRange(Tasks(applicant.Id, applications));

            await _context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Seeded demo account {Email}: {Applications} applications, {Companies} companies.",
                email, applications.Count, companies.Count);
        }

        // ------------------------------------------------------------------

        private static List<Company> Companies(string userId) => new()
        {
            New(userId, "Contoso Cloud", "Managed Kubernetes for European mid-market", "https://contoso.example", "Madrid, Spain"),
            New(userId, "Fabrikam Analytics", "Product analytics for retail", "https://fabrikam.example", "Barcelona, Spain"),
            New(userId, "Northwind Software", "Logistics platform", "https://northwind.example", "Remote"),
            New(userId, "Tailspin Toys", "Consumer e-commerce", "https://tailspin.example", "Valencia, Spain"),
            New(userId, "Proseware Health", "Clinical scheduling", "https://proseware.example", "Lisbon, Portugal"),
            New(userId, "Adventure Works", "Outdoor equipment retail", "https://adventureworks.example", "Bilbao, Spain"),
            New(userId, "Litware Payments", "Payment orchestration", "https://litware.example", "Remote"),
            New(userId, "Wingtip Media", "Streaming infrastructure", "https://wingtip.example", "Seville, Spain")
        };

        private static Company New(string userId, string name, string description, string website, string address) => new()
        {
            UserId = userId,
            Name = name,
            Description = description,
            Website = website,
            Address = address,
            Country = address.Contains("Portugal") ? "Portugal" : "Spain",
            Labels = new List<string> { address == "Remote" ? "remote" : "on-site" }
        };

private static List<Recruiter> Recruiters(string userId, List<Company> companies) => new()
        {
            new()
            {
                UserId = userId, Name = "Elena", Lastname = "Marchetti",
                ContactEmail = "elena.marchetti@example.com", Cellphone = "+34 600 111 222",
                CompanyName = companies[0].Name, LinkedInUrl = "https://www.linkedin.com/in/example-elena"
            },
            new()
            {
                UserId = userId, Name = "Tom", Lastname = "Vance",
                ContactEmail = "tom.vance@example.com", IsFromConsultancy = true,
                CompanyName = "Vance Talent Partners"
            },
            new()
            {
                UserId = userId, Name = "Priya", Lastname = "Raman",
                ContactEmail = "priya.raman@example.com",
                CompanyName = companies[4].Name
            }
        };

        /// <summary>
        /// Spread across every status, so the board has something in each
        /// column rather than a single crowded one.
        /// </summary>
        private static List<JobApplication> Applications(
            string applicantId, List<Company> companies, List<Recruiter> recruiters)
        {
            var now = DateTime.UtcNow;

JobApplication App(
                Company company, string title, JobApplicationStatus status, int appliedDaysAgo,
                decimal salary = 0, List<string>? steps = null, List<string>? notes = null,
                List<string>? labels = null, string? recruiterId = null, string offer = "") => new()
            {
                ApplicantId = applicantId,
                CompanyId = company.Id,
                Company = new CompanySnapshot { Id = company.Id, Name = company.Name },
                JobTitle = title,
                Status = status,
                Source = JobSource.LinkedIn,
                DateApplied = now.AddDays(-appliedDaysAgo),
                CreatedOnDate = now.AddDays(-appliedDaysAgo),
                Salary = salary,
                JobOffer = offer,
                Location = company.Address,
                IsDirect = true,
                URL = $"{company.Website}/careers",
                Notes = notes ?? new List<string>(),
                Labels = labels ?? new List<string>(),
                MainRecruiterContactId = recruiterId ?? string.Empty,
                RecruiterIds = recruiterId is null ? new List<string>() : new List<string> { recruiterId },
                InterviewSteps = (steps ?? new List<string>())
                    .Select((name, i) => new InterviewStep
                    {
                        StepName = name,
                        StepOrder = i + 1,
                        ScheduledDate = now.AddDays(-appliedDaysAgo + ((i + 1) * 5)),
                        CompletedDate = now.AddDays(-appliedDaysAgo + ((i + 1) * 5)),
                        IsCompleted = true
                    }).ToList()
            };

// One in every status, so the board has something in each column
            // rather than a single crowded one.
            return new List<JobApplication>
            {
                App(companies[0], "Senior .NET Engineer", JobApplicationStatus.Meetings, 38, 66000,
                    new List<string> { "Recruiter screen", "Technical interview", "System design" },
                    new List<string> { "Team of six, mostly Azure.", "They asked about Cosmos partitioning - went well." },
                    new List<string> { "favourite", "hybrid" }, recruiters[0].Id),

                App(companies[1], "Backend Engineer, Data Platform", JobApplicationStatus.FirstInterview, 24, 55000,
                    new List<string> { "Intro call" },
                    new List<string> { "Take-home is a streaming aggregation exercise." },
                    new List<string> { "take-home" }, recruiters[1].Id),

                App(companies[4], "Platform Engineer", JobApplicationStatus.FirstInterview, 12, 58000,
                    new List<string> { "Recruiter screen" },
                    new List<string> { "Lisbon, would need relocation or full remote." },
                    new List<string> { "relocation" }, recruiters[2].Id),

                App(companies[6], "Senior Backend Engineer", JobApplicationStatus.Offer, 52, 68000,
                    new List<string> { "Screen", "Technical", "Team fit", "Founder chat" },
                    new List<string> { "Offer expires in ten days.", "Asked for a week to decide." },
                    new List<string> { "offer", "remote" }, null, "68,000 EUR + 0.15% equity"),

                App(companies[2], "Backend Engineer", JobApplicationStatus.Rejected, 61, 0,
                    new List<string> { "Screen", "Technical" },
                    new List<string> { "Rejected after the technical - they wanted more Go." }),

                App(companies[3], ".NET Developer", JobApplicationStatus.Applied, 4, 52000),
                App(companies[5], "Software Engineer, Checkout", JobApplicationStatus.Applied, 2, 54000),

                App(companies[7], "Senior Engineer, Video Pipeline", JobApplicationStatus.Applied, 1, 60000,
                    null, null, new List<string> { "remote" }),

                App(companies[3], "Engineering Manager", JobApplicationStatus.NoResponse, 45, 0, null,
                    new List<string> { "Three weeks, no reply. Chasing once more then dropping it." }),

                App(companies[5], "Backend Engineer (contract)", JobApplicationStatus.NotApplied, 6, 0,
                    null, new List<string> { "Six month contract. Worth a look if the offer falls through." },
                    new List<string> { "contract" }),

                App(companies[2], "Senior Platform Engineer", JobApplicationStatus.Completed, 90, 61000,
                    new List<string> { "Screen", "Technical", "Final" },
                    new List<string> { "Closed out - they filled it internally." })
            };
        }

        private static List<Meeting> Meetings(string applicantId, List<JobApplication> applications)
        {
            var now = DateTime.UtcNow;

            return new List<Meeting>
            {
                new()
                {
                    ApplicantId = applicantId,
                    JobApplicationId = applications[0].Id,
                    Title = "Final interview - Contoso Cloud",
                    Description = "Panel with the platform team.",
                    StartDateTime = now.AddDays(3).Date.AddHours(10),
                    EndDateTime = now.AddDays(3).Date.AddHours(11),
                    OnlineMeetingURL = "https://meet.example.com/contoso-final",
                    SendReminderEmail = true,
                    ReminderMinutesBefore = 60
                },
                new()
                {
                    ApplicantId = applicantId,
                    JobApplicationId = applications[2].Id,
                    Title = "First interview - Proseware Health",
                    StartDateTime = now.AddDays(6).Date.AddHours(15),
                    EndDateTime = now.AddDays(6).Date.AddHours(16),
                    OnlineMeetingURL = "https://meet.example.com/proseware-1",
                    SendReminderEmail = true,
                    ReminderMinutesBefore = 120
                },
                new()
                {
                    ApplicantId = applicantId,
                    JobApplicationId = applications[3].Id,
                    Title = "Offer discussion - Litware Payments",
                    StartDateTime = now.AddDays(-2).Date.AddHours(17),
                    EndDateTime = now.AddDays(-2).Date.AddHours(17).AddMinutes(30),
                    IsPhonecall = true,
                    Outcome = "Offer walked through. Equity vests over four years.",
                    ReminderSent = true
                }
            };
        }

        private static List<ApplicantTask> Tasks(string applicantId, List<JobApplication> applications)
        {
            var now = DateTime.UtcNow;

            return new List<ApplicantTask>
            {
                new()
                {
                    ApplicantId = applicantId,
                    JobApplicationId = applications[1].Id,
                    Type = TaskType.CompleteExercise,
                    Title = "Finish the Fabrikam take-home",
                    Note = "Streaming aggregation exercise. Four hours, roughly.",
                    StartDate = now.AddDays(-2),
                    Deadline = now.AddDays(1).Date.AddHours(18)
                },
                new()
                {
                    ApplicantId = applicantId,
                    JobApplicationId = applications[3].Id,
                    Type = TaskType.FollowUp,
                    Title = "Reply to the Litware offer",
                    Note = "Asked for a week; decide by Friday.",
                    StartDate = now.AddDays(-2),
                    Deadline = now.AddDays(4).Date.AddHours(12)
                },
                new()
                {
                    ApplicantId = applicantId,
                    JobApplicationId = applications[0].Id,
                    Type = TaskType.PrepareForInterview,
                    Title = "Prepare system design for Contoso",
                    Note = "Multi-region write patterns.",
                    StartDate = now.AddDays(-1),
                    Deadline = now.AddDays(2).Date.AddHours(20)
                },
                new()
                {
                    ApplicantId = applicantId,
                    JobApplicationId = applications[0].Id,
                    Type = TaskType.FollowUp,
                    Title = "Send thank-you note to Elena",
                    StartDate = now.AddDays(-10),
                    Deadline = now.AddDays(-9),
                    Completed = true,
                    ReminderSent = true
                },
                new()
                {
                    ApplicantId = applicantId,
                    JobApplicationId = applications[4].Id,
                    Type = TaskType.ReachOut,
                    Title = "Ask Northwind for feedback",
                    StartDate = now.AddDays(-20),
                    Deadline = now.AddDays(-18),
                    Completed = true,
                    ReminderSent = true
                }
            };
        }
    }
}
