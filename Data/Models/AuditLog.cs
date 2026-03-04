using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DMS_CPMS.Data.Models
{
    [Table("AuditLog")]
    public class AuditLog
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int LogID { get; set; }

        public int? DocumentID { get; set; }

        [StringLength(450)]
        public string? UserID { get; set; }

        [StringLength(50)]
        public string? Role { get; set; }

        [Required]
        [StringLength(100)]
        public string Action { get; set; } = string.Empty;

        [Required]
        public DateTime Timestamp { get; set; }

        // Navigation properties
        [ForeignKey("DocumentID")]
        public virtual Document? Document { get; set; }

        [ForeignKey("UserID")]
        public virtual ApplicationUser? User { get; set; }
    }
}
