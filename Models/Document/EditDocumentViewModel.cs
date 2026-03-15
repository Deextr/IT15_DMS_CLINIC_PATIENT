using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;
using DMS_CPMS.Validation;

namespace DMS_CPMS.Models.Document
{
    public class EditDocumentViewModel
    {
        [Required]
        public int DocumentID { get; set; }

        [Required]
        public int PatientID { get; set; }

        [Required]
        [StringLength(100)]
        public string DocumentTitle { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string DocumentType { get; set; } = string.Empty;

        // Optional new file to create a new version
        [MaxFileSize(10 * 1024 * 1024)]
        public IFormFile? NewFile { get; set; }
    }
}
