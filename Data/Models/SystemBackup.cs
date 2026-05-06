using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DMS_CPMS.Data.Models
{
    [Table("SystemBackup")]
    public class SystemBackup
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int SystemBackupId { get; set; }

        [Required]
        [StringLength(260)]
        public string FileName { get; set; } = string.Empty; // clinixdocs_backup_YYYYMMDD_HHMMSS.enc

        [Required]
        [StringLength(400)]
        public string StoragePath { get; set; } = string.Empty; // local absolute path or cloud object key

        [Required]
        [StringLength(50)]
        public string StorageProvider { get; set; } = "Local"; // Local|S3 (future)

        [Required]
        public long SizeBytes { get; set; }

        [Required]
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        [StringLength(450)]
        public string? CreatedByUserId { get; set; }

        [StringLength(50)]
        public string? CreatedByRole { get; set; }

        [StringLength(500)]
        public string? Notes { get; set; }
    }
}

