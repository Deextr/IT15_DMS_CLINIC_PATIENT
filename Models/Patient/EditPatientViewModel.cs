using System;
using System.ComponentModel.DataAnnotations;

using DMS_CPMS.Validation;

namespace DMS_CPMS.Models.Patient
{
    public class EditPatientViewModel
    {
        [Required]
        public int PatientID { get; set; }

        [Required]
        [StringLength(50)]
        public string FirstName { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string LastName { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Date)]
        [ValidBirthDate]
        public DateTime BirthDate { get; set; }

        [Required]
        [StringLength(10)]
        [RegularExpression(@"^(Male|Female|Other)$", ErrorMessage = "Gender must be Male, Female, or Other.")]
        public string Gender { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Last Visited")]
        [DataType(DataType.DateTime)]
        public DateTime VisitedAt { get; set; }
    }
}
