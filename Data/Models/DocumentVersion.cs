using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DMS_CPMS.Data.Models
{
    [Table("DocumentVersion")]
    public class DocumentVersion
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int VersionID { get; set; }

        [Required]
        [ForeignKey("Document")]
        public int DocumentID { get; set; }

        [Required]
        public int VersionNumber { get; set; }

        [Required]
        [StringLength(255)]
        public string FilePath { get; set; } = string.Empty;

        [Required]
        public DateTime CreatedDate { get; set; }

        // Navigation property
        public virtual Document Document { get; set; }
    }
}