using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

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
        public DateTime BirthDate { get; set; }

        [Required]
        [StringLength(10)]
        public string Gender { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Last Visited")]
        [DataType(DataType.DateTime)]
        public DateTime VisitedAt { get; set; }

        // Optional initial documents to upload for the patient
        public List<IFormFile>? UploadedFiles { get; set; }
    }
}
