using System;
using System.ComponentModel.DataAnnotations;

namespace DMS_CPMS.Validation
{
    public class ValidBirthDateAttribute : ValidationAttribute
    {
        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value is DateTime date)
            {
                if (date > DateTime.Today)
                    return new ValidationResult("Birth date cannot be in the future.");

                if (date < new DateTime(1900, 1, 1))
                    return new ValidationResult("Birth date must be after January 1, 1900.");
            }

            return ValidationResult.Success;
        }
    }
}
