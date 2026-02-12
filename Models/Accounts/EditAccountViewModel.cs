using System.ComponentModel.DataAnnotations;

namespace DMS_CPMS.Models.Accounts
{
    public class EditAccountViewModel
    {
        public string Id { get; set; } = string.Empty;

        [Required(ErrorMessage = "First name is required.")]
        [Display(Name = "First Name")]
        [StringLength(100)]
        public string FirstName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Last name is required.")]
        [Display(Name = "Last Name")]
        [StringLength(100)]
        public string LastName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Username is required.")]
        [Display(Name = "Username")]
        [StringLength(256)]
        public string Username { get; set; } = string.Empty;

        /// <summary>Admin or Staff for SuperAdmin; Staff only for Admin area.</summary>
        [Required]
        [Display(Name = "Role")]
        public string RoleType { get; set; } = "Staff";

        [Display(Name = "Account is active")]
        public bool IsActive { get; set; } = true;

        [DataType(DataType.Password)]
        [Display(Name = "New Password (leave blank to keep current)")]
        [StringLength(100, ErrorMessage = "Password must be at least {2} characters long.", MinimumLength = 8)]
        [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z\d]).+$",
            ErrorMessage = "Password must contain uppercase, lowercase, number, and special character.")]
        public string? NewPassword { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "Confirm New Password")]
        [Compare("NewPassword", ErrorMessage = "Password and confirmation do not match.")]
        public string? ConfirmPassword { get; set; }
    }
}
