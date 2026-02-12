using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace DMS_CPMS.Models.Document
{
    public class AddDocumentViewModel
    {
        [Required]
        public int PatientID { get; set; }

        [Required]
        [StringLength(100)]
        public string DocumentTitle { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string DocumentType { get; set; } = string.Empty;

        [Required]
        public IFormFile? File { get; set; }
    }
}
