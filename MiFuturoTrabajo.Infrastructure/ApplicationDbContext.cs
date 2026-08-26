using Microsoft.EntityFrameworkCore;
using MiFuturoTrabajo.Infrastructure.Entities;

namespace MiFuturoTrabajo.Infrastructure
{
    /// <summary>
    /// Cosmos DB model.
    ///
    /// The provider cannot join documents across containers, so anything that
    /// is always read together with its parent is *embedded* as an owned type
    /// rather than modelled as a cross-container relationship. That collapses
    /// twelve containers to seven and turns the board view into a single
    /// single-partition query.
    ///
    /// Container        Partition key   Embedded
    /// ---------------  --------------  ------------------------------------
    /// Users            /Email          -
    /// Applicants       /UserId         CVVersions, PastJobReferences
    /// JobApplications  /ApplicantId    InterviewSteps, Company snapshot,
    ///                                  Notes, Labels, RecruiterIds
    /// Meetings         /ApplicantId    RecruiterIds
    /// ApplicantTasks   /ApplicantId    -
    /// Companies        /UserId         Labels
    /// Recruiters       /UserId         -
    /// </summary>
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<Applicant> Applicants { get; set; }
        public DbSet<Company> Companies { get; set; }
        public DbSet<Recruiter> Recruiters { get; set; }
        public DbSet<JobApplication> JobApplications { get; set; }
        public DbSet<Meeting> Meetings { get; set; }
        public DbSet<ApplicantTask> ApplicantTasks { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ---------------------------------------------------------------
            // Users - partitioned by email because login is the hot path and
            // becomes a point read. The JWT also carries an email claim, so
            // authenticated lookups can be point reads too.
            //
            // Note: no unique index on Email. The Cosmos provider does not
            // enforce unique indexes, so declaring one was a false guarantee.
            // Uniqueness comes from the partition key plus a pre-insert check.
            // ---------------------------------------------------------------
            modelBuilder.Entity<User>(entity =>
            {
                entity.ToContainer("Users");
                entity.HasKey(e => e.Id);
                entity.HasPartitionKey(e => e.Email);
            });

            // ---------------------------------------------------------------
            // Applicants
            // ---------------------------------------------------------------
            modelBuilder.Entity<Applicant>(entity =>
            {
                entity.ToContainer("Applicants");
                entity.HasKey(e => e.Id);
                entity.HasPartitionKey(e => e.UserId);

                entity.OwnsMany(e => e.CVVersions);
                entity.OwnsMany(e => e.PastJobReferences);
            });

            // ---------------------------------------------------------------
            // JobApplications
            // ---------------------------------------------------------------
            modelBuilder.Entity<JobApplication>(entity =>
            {
                entity.ToContainer("JobApplications");
                entity.HasKey(e => e.Id);
                entity.HasPartitionKey(e => e.ApplicantId);

                entity.OwnsMany(e => e.InterviewSteps);
                entity.OwnsOne(e => e.Company);
            });

            // ---------------------------------------------------------------
            // Meetings
            // ---------------------------------------------------------------
            modelBuilder.Entity<Meeting>(entity =>
            {
                entity.ToContainer("Meetings");
                entity.HasKey(e => e.Id);
                entity.HasPartitionKey(e => e.ApplicantId);
            });

            // ---------------------------------------------------------------
            // ApplicantTasks
            // ---------------------------------------------------------------
            modelBuilder.Entity<ApplicantTask>(entity =>
            {
                entity.ToContainer("ApplicantTasks");
                entity.HasKey(e => e.Id);
                entity.HasPartitionKey(e => e.ApplicantId);
            });

            // ---------------------------------------------------------------
            // Companies
            // ---------------------------------------------------------------
            modelBuilder.Entity<Company>(entity =>
            {
                entity.ToContainer("Companies");
                entity.HasKey(e => e.Id);
                entity.HasPartitionKey(e => e.UserId);
            });

            // ---------------------------------------------------------------
            // Recruiters
            // ---------------------------------------------------------------
            modelBuilder.Entity<Recruiter>(entity =>
            {
                entity.ToContainer("Recruiters");
                entity.HasKey(e => e.Id);
                entity.HasPartitionKey(e => e.UserId);
            });
        }
    }
}
