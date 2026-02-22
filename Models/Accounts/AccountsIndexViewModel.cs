namespace DMS_CPMS.Models.Accounts
{
    public class AccountsIndexViewModel
    {
        public List<AccountListItemViewModel> Accounts { get; set; } = new List<AccountListItemViewModel>();
        
        // Search/Filter properties
        public string? SearchTerm { get; set; }
        public string? RoleFilter { get; set; }
        public string? StatusFilter { get; set; }
        
        // Pagination
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public int TotalPages { get; set; } = 1;
        public int TotalCount { get; set; } = 0;
        
        // For creating new account
        public SuperAdmin.CreateAccountViewModel NewAccount { get; set; } = new SuperAdmin.CreateAccountViewModel();
    }
}
