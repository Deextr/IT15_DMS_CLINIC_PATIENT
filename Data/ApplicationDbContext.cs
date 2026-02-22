using DMS_CPMS.Data.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DMS_CPMS.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, string>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Patient> Patients { get; set; }
        public DbSet<Document> Documents { get; set; }
        public DbSet<DocumentVersion> DocumentVersions { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<ArchiveDocument> ArchiveDocuments { get; set; }
        public DbSet<RetentionPolicy> RetentionPolicies { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // ApplicationUser configuration
            builder.Entity<ApplicationUser>(entity =>
            {
                entity.Property(e => e.FirstName)
                    .IsRequired()
                    .HasMaxLength(100);
                entity.Property(e => e.LastName)
                    .IsRequired()
                    .HasMaxLength(100);
                entity.Property(e => e.IsActive)
                    .IsRequired()
                    .HasDefaultValue(true);
            });

            // Patient configuration
            builder.Entity<Patient>(entity =>
            {
                entity.HasKey(e => e.PatientID);
                entity.Property(e => e.FirstName).IsRequired().HasMaxLength(50);
                entity.Property(e => e.LastName).IsRequired().HasMaxLength(50);
                entity.Property(e => e.BirthDate).IsRequired();
                entity.Property(e => e.Gender).IsRequired().HasMaxLength(10);
                entity.Property(e => e.VisitedAt)
                      .IsRequired()
                      .HasDefaultValueSql("GETDATE()");
            });

            // Document configuration
            builder.Entity<Document>(entity =>
            {
                entity.HasKey(e => e.DocumentID);
                entity.Property(e => e.DocumentTitle).IsRequired().HasMaxLength(100);
                entity.Property(e => e.DocumentType).IsRequired().HasMaxLength(50);
                entity.Property(e => e.UploadDate).IsRequired();
                entity.Property(e => e.UploadBy).IsRequired();

                // Foreign key relationships
                entity.HasOne(e => e.Patient)
                    .WithMany(p => p.Documents)
                    .HasForeignKey(e => e.PatientID)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.UploadedByUser)
                    .WithMany()
                    .HasForeignKey(e => e.UploadBy)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // DocumentVersion configuration
            builder.Entity<DocumentVersion>(entity =>
            {
                entity.HasKey(e => e.VersionID);
                entity.Property(e => e.FilePath).IsRequired().HasMaxLength(255);
                entity.Property(e => e.VersionNumber).IsRequired();
                entity.Property(e => e.CreatedDate).IsRequired();

                entity.HasOne(e => e.Document)
                    .WithMany(d => d.Versions)
                    .HasForeignKey(e => e.DocumentID)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // AuditLog configuration
            builder.Entity<AuditLog>(entity =>
            {
                entity.HasKey(e => e.AuditLogID);
                entity.Property(e => e.Action).IsRequired().HasMaxLength(50);
                entity.Property(e => e.EntityType).IsRequired().HasMaxLength(50);
                entity.Property(e => e.EntityId).IsRequired();
                entity.Property(e => e.Details).HasMaxLength(500);
                entity.Property(e => e.UserId).HasMaxLength(100);
                entity.Property(e => e.UserName).HasMaxLength(100);
                entity.Property(e => e.Timestamp).IsRequired();
                entity.Property(e => e.IpAddress).HasMaxLength(50);
            });

            // Document – IsArchived flag
            builder.Entity<Document>(entity =>
            {
                entity.Property(e => e.IsArchived)
                    .IsRequired()
                    .HasDefaultValue(false);
            });

            // ArchiveDocument configuration
            builder.Entity<ArchiveDocument>(entity =>
            {
                entity.HasKey(e => e.ArchiveID);

                entity.Property(e => e.ArchiveReason)
                    .IsRequired()
                    .HasMaxLength(200);

                entity.Property(e => e.ArchiveDate)
                    .IsRequired();

                entity.Property(e => e.RetentionUntil)
                    .IsRequired();

                // One-to-many: a Document can have multiple archive records
                entity.HasOne(e => e.Document)
                    .WithMany(d => d.ArchiveDocuments)
                    .HasForeignKey(e => e.DocumentID)
                    .OnDelete(DeleteBehavior.Restrict);

                // Optional FK to DocumentVersion (null = document-level archive)
                entity.HasOne(e => e.ArchivedVersion)
                    .WithMany()
                    .HasForeignKey(e => e.VersionID)
                    .IsRequired(false)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(e => e.ArchivedByUser)
                    .WithMany()
                    .HasForeignKey(e => e.UserID)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // RetentionPolicy configuration
            builder.Entity<RetentionPolicy>(entity =>
            {
                entity.HasKey(e => e.RetentionPolicyID);

                entity.Property(e => e.ModuleName)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(e => e.RetentionDurationMonths)
                    .IsRequired();

                entity.Property(e => e.AutoActionAfterExpiry)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.Property(e => e.IsEnabled)
                    .IsRequired()
                    .HasDefaultValue(true);
            });
        }
    }
}
