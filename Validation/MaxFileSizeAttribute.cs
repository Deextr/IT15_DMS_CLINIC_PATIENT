using System.ComponentModel.DataAnnotations;

namespace DMS_CPMS.Validation
{
    /// <summary>
    /// Validates that an IFormFile (or each file in a List&lt;IFormFile&gt;) does not exceed the specified size in bytes.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class MaxFileSizeAttribute : ValidationAttribute
    {
        private readonly long _maxBytes;

        public MaxFileSizeAttribute(long maxBytes)
        {
            _maxBytes = maxBytes;
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            var maxMb = _maxBytes / (1024.0 * 1024.0);

            if (value is IFormFile singleFile)
            {
                if (singleFile.Length > _maxBytes)
                    return new ValidationResult($"File \"{singleFile.FileName}\" exceeds the maximum allowed size of {maxMb:F0} MB.");
            }
            else if (value is List<IFormFile> files)
            {
                foreach (var file in files)
                {
                    if (file.Length > _maxBytes)
                        return new ValidationResult($"File \"{file.FileName}\" exceeds the maximum allowed size of {maxMb:F0} MB.");
                }
            }

            return ValidationResult.Success;
        }
    }
}
