using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using DMS_CPMS.Validation;

namespace DMS_CPMS.Models.Patient
{
    public class CreatePatientViewModel
    {
        [Required]
        [StringLength(50)]
        public string FirstName { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string LastName { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Date)]
        [ValidBirthDate]
        public DateTime BirthDate { get; set; } = DateTime.Today;

        [Required]
        [StringLength(10)]
        [RegularExpression(@"^(Male|Female|Other)$", ErrorMessage = "Gender must be Male, Female, or Other.")]
        public string Gender { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Last Visited")]
        [DataType(DataType.DateTime)]
        public DateTime VisitedAt { get; set; }

        // Optional initial document to upload for the patient
        [MaxFileSize(10 * 1024 * 1024)]
        public IFormFile? UploadedFile { get; set; }

        // Google Drive upload fields
        public string? DocumentSourceType { get; set; }
        public string? GoogleDriveFileId { get; set; }
        public string? GoogleDriveAccessToken { get; set; }
    }
}
