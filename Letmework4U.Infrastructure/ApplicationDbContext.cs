using Microsoft.EntityFrameworkCore;
using Letmework4U.Infrastructure.Entities;

namespace Letmework4U.Infrastructure
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // DbSets
        public DbSet<User> Users { get; set; }
        public DbSet<Applicant> Applicants { get; set; }
        public DbSet<CVVersion> CVVersions { get; set; }
        public DbSet<JobReference> JobReferences { get; set; }
        public DbSet<Company> Companies { get; set; }
        public DbSet<Recruiter> Recruiters { get; set; }
        public DbSet<JobApplication> JobApplications { get; set; }
        public DbSet<JobApplicationRecruiter> JobApplicationRecruiters { get; set; }
        public DbSet<InterviewStep> InterviewSteps { get; set; }
        public DbSet<Meeting> Meetings { get; set; }
        public DbSet<MeetingRecruiter> MeetingRecruiters { get; set; }
        public DbSet<ApplicantTask> ApplicantTasks { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // User Configuration (for proprietary authentication)
            modelBuilder.Entity<User>(entity =>
            {
                entity.ToContainer("Users");
                entity.HasKey(e => e.Id);
                entity.HasPartitionKey(e => e.Email); // Partition by email for efficient login queries

                // Unique email constraint
                entity.HasIndex(e => e.Email).IsUnique();

                // One-to-one relationship with Applicant
                entity.HasOne(e => e.Applicant)
                    .WithOne()
                    .HasForeignKey<User>(e => e.ApplicantId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // Applicant Configuration
            modelBuilder.Entity<Applicant>(entity =>
            {
                entity.ToContainer("Applicants");
                entity.HasKey(e => e.Id);
                entity.HasPartitionKey(e => e.UserId);

                entity.HasMany(e => e.CVVersions)
                    .WithOne(e => e.Applicant)
                    .HasForeignKey(e => e.ApplicantId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasMany(e => e.PastJobReferences)
                    .WithOne(e => e.Applicant)
                    .HasForeignKey(e => e.ApplicantId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasMany(e => e.JobApplications)
                    .WithOne(e => e.Applicant)
                    .HasForeignKey(e => e.ApplicantId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasMany(e => e.Meetings)
                    .WithOne(e => e.Applicant)
                    .HasForeignKey(e => e.ApplicantId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasMany(e => e.Tasks)
                    .WithOne(e => e.Applicant)
                    .HasForeignKey(e => e.ApplicantId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // CVVersion Configuration
            modelBuilder.Entity<CVVersion>(entity =>
            {
                entity.ToContainer("CVVersions");
                entity.HasKey(e => e.Id);
                entity.HasPartitionKey(e => e.ApplicantId);
            });

            // JobReference Configuration
            modelBuilder.Entity<JobReference>(entity =>
            {
                entity.ToContainer("JobReferences");
                entity.HasKey(e => e.Id);
                entity.HasPartitionKey(e => e.ApplicantId);
            });

            // Company Configuration
            modelBuilder.Entity<Company>(entity =>
            {
                entity.ToContainer("Companies");
                entity.HasKey(e => e.Id);
                entity.HasPartitionKey(e => e.UserId);

                entity.HasMany(e => e.JobApplications)
                    .WithOne(e => e.Company)
                    .HasForeignKey(e => e.CompanyId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // Recruiter Configuration
            modelBuilder.Entity<Recruiter>(entity =>
            {
                entity.ToContainer("Recruiters");
                entity.HasKey(e => e.Id);
                entity.HasPartitionKey(e => e.UserId);

                entity.HasMany(e => e.JobApplicationRecruiters)
                    .WithOne(e => e.Recruiter)
                    .HasForeignKey(e => e.RecruiterId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasMany(e => e.MeetingRecruiters)
                    .WithOne(e => e.Recruiter)
                    .HasForeignKey(e => e.RecruiterId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // JobApplication Configuration
            modelBuilder.Entity<JobApplication>(entity =>
            {
                entity.ToContainer("JobApplications");
                entity.HasKey(e => e.Id);
                entity.HasPartitionKey(e => e.ApplicantId);

                entity.HasMany(e => e.JobApplicationRecruiters)
                    .WithOne(e => e.JobApplication)
                    .HasForeignKey(e => e.JobApplicationId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasMany(e => e.ApplicantTasks)
                    .WithOne(e => e.JobApplication)
                    .HasForeignKey(e => e.JobApplicationId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasMany(e => e.InterviewSteps)
                    .WithOne(e => e.JobApplication)
                    .HasForeignKey(e => e.JobApplicationId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasMany(e => e.Meetings)
                    .WithOne(e => e.JobApplication)
                    .HasForeignKey(e => e.JobApplicationId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // JobApplicationRecruiter Configuration (Many-to-Many)
            modelBuilder.Entity<JobApplicationRecruiter>(entity =>
            {
                entity.ToContainer("JobApplicationRecruiters");
                entity.HasKey(e => new { e.JobApplicationId, e.RecruiterId });
                entity.HasPartitionKey(e => e.JobApplicationId);
            });

            // InterviewStep Configuration
            modelBuilder.Entity<InterviewStep>(entity =>
            {
                entity.ToContainer("InterviewSteps");
                entity.HasKey(e => e.Id);
                entity.HasPartitionKey(e => e.JobApplicationId);
            });

            // Meeting Configuration
            modelBuilder.Entity<Meeting>(entity =>
            {
                entity.ToContainer("Meetings");
                entity.HasKey(e => e.Id);
                entity.HasPartitionKey(e => e.ApplicantId);

                entity.HasMany(e => e.MeetingRecruiters)
                    .WithOne(e => e.Meeting)
                    .HasForeignKey(e => e.MeetingId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // MeetingRecruiter Configuration (Many-to-Many)
            modelBuilder.Entity<MeetingRecruiter>(entity =>
            {
                entity.ToContainer("MeetingRecruiters");
                entity.HasKey(e => new { e.MeetingId, e.RecruiterId });
                entity.HasPartitionKey(e => e.MeetingId);
            });

            // ApplicantTask Configuration
            modelBuilder.Entity<ApplicantTask>(entity =>
            {
                entity.ToContainer("ApplicantTasks");
                entity.HasKey(e => e.Id);
                entity.HasPartitionKey(e => e.ApplicantId);
            });
        }
    }
}
