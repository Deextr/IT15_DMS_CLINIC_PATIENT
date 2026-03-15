using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using DMS_CPMS.Validation;

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

        [MaxFileSize(10 * 1024 * 1024)]
        public IFormFile? File { get; set; }

        [MaxFileSize(10 * 1024 * 1024)]
        public List<IFormFile>? Files { get; set; }
    }
}
