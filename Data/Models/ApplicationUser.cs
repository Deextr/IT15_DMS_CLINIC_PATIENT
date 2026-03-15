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

        public bool IsArchived { get; set; } = false;

        /// <summary>Relative URL to the user's uploaded profile picture, e.g. /uploads/profile-pictures/abc.jpg</summary>
        [StringLength(500)]
        public string? ProfilePictureUrl { get; set; }
    }
}