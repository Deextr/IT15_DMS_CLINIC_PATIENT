using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DMS_CPMS.Data.Models
{
    [Table("Document")]
    public class Document
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int DocumentID { get; set; }

        [Required]
        [ForeignKey("Patient")]
        public int PatientID { get; set; }

        [Required]
        [ForeignKey("UploadedByUser")]
        public string UploadBy { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string DocumentTitle { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string DocumentType { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Date)]
        public DateTime UploadDate { get; set; }

        /// <summary>
        /// Indicates whether the document has been archived.
        /// Archived documents are excluded from active modules.
        /// </summary>
        public bool IsArchived { get; set; } = false;

        // Navigation properties
        public virtual Patient Patient { get; set; }
        public virtual ApplicationUser UploadedByUser { get; set; }
        public virtual ICollection<DocumentVersion> Versions { get; set; } = new List<DocumentVersion>();
        public virtual ICollection<ArchiveDocument> ArchiveDocuments { get; set; } = new List<ArchiveDocument>();
    }
}