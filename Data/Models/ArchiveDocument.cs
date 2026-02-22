using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DMS_CPMS.Data.Models
{
    [Table("ArchiveDocument")]
    public class ArchiveDocument
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int ArchiveID { get; set; }

        [Required]
        [ForeignKey("Document")]
        public int DocumentID { get; set; }

        /// <summary>
        /// Nullable FK to DocumentVersion. When set, this is a version-level archive.
        /// When null, the entire document was archived.
        /// </summary>
        [ForeignKey("ArchivedVersion")]
        public int? VersionID { get; set; }

        [Required]
        [ForeignKey("ArchivedByUser")]
        public string UserID { get; set; } = string.Empty;

        [Required]
        [StringLength(200)]
        public string ArchiveReason { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Date)]
        public DateTime ArchiveDate { get; set; }

        [Required]
        [DataType(DataType.Date)]
        public DateTime RetentionUntil { get; set; }

        // Navigation properties
        public virtual Document Document { get; set; }
        public virtual DocumentVersion? ArchivedVersion { get; set; }
        public virtual ApplicationUser ArchivedByUser { get; set; }
    }
}
