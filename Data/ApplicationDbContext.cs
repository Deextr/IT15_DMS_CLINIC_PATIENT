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
        }
    }
}
