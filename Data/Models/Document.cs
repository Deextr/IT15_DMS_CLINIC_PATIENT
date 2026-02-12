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

        // Navigation properties
        public virtual Patient Patient { get; set; }
        public virtual ApplicationUser UploadedByUser { get; set; }
        public virtual ICollection<DocumentVersion> Versions { get; set; } = new List<DocumentVersion>();
    }
}