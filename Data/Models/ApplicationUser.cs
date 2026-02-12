using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace DMS_CPMS.Data.Models
{
    public class ApplicationUser : IdentityUser
    {
        [Required]
        [StringLength(100)]
        public string FirstName { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string LastName { get; set; } = string.Empty;

        // Controls whether the user is allowed to log in.
        public bool IsActive { get; set; } = true;
    }
}