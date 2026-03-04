using System.ComponentModel.DataAnnotations;

namespace DMS_CPMS.Models.Profile
{
    public class ProfileViewModel
    {
        public string UserId { get; set; } = string.Empty;

        /// <summary>Display-only – never editable by the user.</summary>
        public string Role { get; set; } = string.Empty;

        // ── Common to ALL roles ──────────────────────────────────────────────
        public string? ProfilePictureUrl { get; set; }

        [Required(ErrorMessage = "Username is required.")]
        [StringLength(50, MinimumLength = 3, ErrorMessage = "Username must be between 3 and 50 characters.")]
        [RegularExpression(@"^[A-Za-z0-9._\-]+$", ErrorMessage = "Only letters, numbers, dots, underscores and hyphens are allowed.")]
        [Display(Name = "Username")]
        public string Username { get; set; } = string.Empty;

        // ── SuperAdmin + Admin only ──────────────────────────────────────────
        [StringLength(50)]
        [Display(Name = "First Name")]
        public string? FirstName { get; set; }

        [StringLength(50)]
        [Display(Name = "Last Name")]
        public string? LastName { get; set; }

        // ── Password change fields (all roles) ───────────────────────────────
        [DataType(DataType.Password)]
        [Display(Name = "Current Password")]
        public string? CurrentPassword { get; set; }

        [StringLength(100, MinimumLength = 8)]
        [DataType(DataType.Password)]
        [Display(Name = "New Password")]
        public string? NewPassword { get; set; }

        [DataType(DataType.Password)]
        [Compare("NewPassword", ErrorMessage = "Passwords do not match.")]
        [Display(Name = "Confirm New Password")]
        public string? ConfirmNewPassword { get; set; }

        // ── Role helpers ─────────────────────────────────────────────────────
        public bool IsSuperAdmin  => Role == "SuperAdmin";
        public bool IsAdmin       => Role == "Admin";
        public bool IsStaff       => Role == "Staff";

        /// <summary>SuperAdmin and Admin can edit Full Name fields.</summary>
        public bool CanEditFullName => IsSuperAdmin || IsAdmin;
    }
}
