using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace DMS_CPMS.Models.Patient
{
    public class PatientDocumentsIndexViewModel
    {
        public Data.Models.Patient Patient { get; set; } = new Data.Models.Patient();
        public int PatientAge => CalculateAge(Patient.BirthDate);
        public List<DocumentViewModel> Documents { get; set; } = new List<DocumentViewModel>();
        
        // Search/Filter properties
        public string? SearchTerm { get; set; }
        public string? DocumentTypeFilter { get; set; }
        public string? StatusFilter { get; set; }
        
        // Pagination
        public int PageNumber { get; set; } = 1;
        public int TotalPages { get; set; } = 1;
        
        // For creating new document
        public UploadDocumentViewModel NewDocument { get; set; } = new UploadDocumentViewModel();
        
        private int CalculateAge(DateTime birthDate)
        {
            var age = DateTime.Now.Year - birthDate.Year;
            if (DateTime.Now.DayOfYear < birthDate.DayOfYear) age--;
            return age;
        }
    }

    public class DocumentViewModel
    {
        public int DocumentID { get; set; }
        public string DocumentName { get; set; } = string.Empty;
        public string DocumentType { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string UploadedBy { get; set; } = string.Empty;
        public DateTime UploadDate { get; set; }
        public string Status { get; set; } = string.Empty;
        public bool IsActive => Status == "Active";
        public string? FilePath { get; set; }
        public string? FileExtension { get; set; }
    }

    public class EditDocumentViewModel
    {
        public int DocumentID { get; set; }

        [Required]
        [StringLength(100)]
        public string DocumentTitle { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string DocumentType { get; set; } = string.Empty;

        [StringLength(30)]
        public string? OtherDocumentType { get; set; }
    }

    public class DocumentVersionViewModel
    {
        public int VersionID { get; set; }
        public string Version { get; set; } = string.Empty;
        public string ModifiedBy { get; set; } = string.Empty;
        public string DateModified { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
    }

    public class UploadDocumentViewModel
    {
        [Required]
        public int PatientID { get; set; }

        [Required]
        [StringLength(100)]
        public string DocumentTitle { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string DocumentType { get; set; } = string.Empty;

        [StringLength(30)]
        public string? OtherDocumentType { get; set; }

        [Required]
        public IFormFile? UploadedFile { get; set; }
    }

    public class EditDocumentWithVersionViewModel
    {
        public int DocumentID { get; set; }

        [Required]
        [StringLength(100)]
        public string DocumentTitle { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string DocumentType { get; set; } = string.Empty;

        [StringLength(30)]
        public string? OtherDocumentType { get; set; }

        public IFormFile? NewVersionFile { get; set; }

        [StringLength(100)]
        public string? VersionNotes { get; set; }
    }
}
