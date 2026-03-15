namespace DMS_CPMS.Models.Accounts
{
    public class AccountListItemViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string FullName => $"{FirstName} {LastName}".Trim();
        public string Role { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public string StatusDisplay => IsActive ? "Active" : "Deactivated";
    }
}
