using System;
using System.Collections.Generic;

namespace DMS_CPMS.Models.AuditLog
{
    public class AuditLogIndexViewModel
    {
        public List<AuditLogItemViewModel> Logs { get; set; } = new();

        // Filters
        public string? SearchTerm { get; set; }
        public string? RoleFilter { get; set; }
        public string? DateFrom { get; set; }
        public string? DateTo { get; set; }

        // Pagination
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 8;
        public int TotalPages { get; set; }
        public int TotalCount { get; set; }
    }

    public class AuditLogItemViewModel
    {
        public int LogID { get; set; }
        public int? DocumentID { get; set; }
        public string? DocumentTitle { get; set; }
        public string? UserID { get; set; }
        public string UserFullName { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }

        public string FormattedDate => Timestamp.ToString("MMM dd, yyyy");
        public string FormattedTime => Timestamp.ToString("hh:mm tt");
    }
}
