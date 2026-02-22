using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace DMS_CPMS.Models.Archive
{
    // ───────────────────────── Index (main page) ─────────────────────────
    public class ArchiveRetentionIndexViewModel
    {
        // Tab 1 – Archived Documents
        public List<ArchivedDocumentViewModel> ArchivedDocuments { get; set; } = new();

        // Tab 2 – Retention Policies
        public List<RetentionPolicyViewModel> RetentionPolicies { get; set; } = new();

        // Stats
        public int TotalDocuments { get; set; }
        public int ActiveRetentionCount { get; set; }
        public int ExpiredRetentionCount { get; set; }
        public int TotalPolicies { get; set; }

        // Pagination – Archived Documents
        public int PageNumber { get; set; } = 1;
        public int TotalPages { get; set; }

        // Pagination – Retention Policies
        public int PolicyPageNumber { get; set; } = 1;
        public int PolicyTotalPages { get; set; }

        // Filters
        public string? SearchTerm { get; set; }
        public string? StatusFilter { get; set; } // "Active", "Expired", or null/all

        // Document types (for policy modal dropdown)
        public List<string> DocumentTypes { get; set; } = new();

        // Forms (for modal binding)
        public ArchiveDocumentFormViewModel NewArchive { get; set; } = new();
        public RetentionPolicyFormViewModel NewPolicy { get; set; } = new();

        // Role flag (set by controller)
        public bool IsSuperAdmin { get; set; }
    }

    // ───────────────────── Archived Document display ─────────────────────
    public class ArchivedDocumentViewModel
    {
        public int ArchiveID { get; set; }
        public int DocumentID { get; set; }
        public string DocumentTitle { get; set; } = string.Empty;
        public string DocumentType { get; set; } = string.Empty;
        public string PatientName { get; set; } = string.Empty;
        public string ArchivedByName { get; set; } = string.Empty;
        public string ArchiveReason { get; set; } = string.Empty;
        public DateTime ArchiveDate { get; set; }
        public DateTime RetentionUntil { get; set; }

        /// <summary>Non-null when this archive is for a specific version rather than the whole document.</summary>
        public int? VersionNumber { get; set; }

        /// <summary>True when VersionNumber has a value (version-level archive).</summary>
        public bool IsVersionArchive => VersionNumber.HasValue;

        /// <summary>True when RetentionUntil >= today.</summary>
        public bool IsWithinRetention => RetentionUntil.Date >= DateTime.Today;

        public string StatusText => IsWithinRetention ? "Active Retention" : "Expired";
        public string StatusBadgeClass => IsWithinRetention ? "bg-success" : "bg-danger";
    }

    // ──────────────────── Archive a Document (form) ──────────────────────
    public class ArchiveDocumentFormViewModel
    {
        [Required]
        public int DocumentID { get; set; }

        [Required(ErrorMessage = "Archive reason is required.")]
        [StringLength(200, ErrorMessage = "Reason cannot exceed 200 characters.")]
        public string ArchiveReason { get; set; } = string.Empty;
    }

    // ──────────────────── Retention Policy display ───────────────────────
    public class RetentionPolicyViewModel
    {
        public int RetentionPolicyID { get; set; }
        public string ModuleName { get; set; } = string.Empty;
        public int RetentionDurationMonths { get; set; }
        public string AutoActionAfterExpiry { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }

        /// <summary>Human-readable duration, e.g. "5 Years", "6 Months".</summary>
        public string DurationDisplay
        {
            get
            {
                if (RetentionDurationMonths % 12 == 0)
                    return $"{RetentionDurationMonths / 12} Year{(RetentionDurationMonths / 12 > 1 ? "s" : "")}";
                return $"{RetentionDurationMonths} Month{(RetentionDurationMonths > 1 ? "s" : "")}";
            }
        }

        public string ActionDisplay => AutoActionAfterExpiry switch
        {
            "NotifyAdmin" => "Notify Admin",
            "AutoDelete" => "Auto Delete",
            "ManualReview" => "Manual Review",
            _ => AutoActionAfterExpiry
        };
    }

    // ──────────────────── Retention Policy form ──────────────────────────
    public class RetentionPolicyFormViewModel
    {
        public int? RetentionPolicyID { get; set; } // null = create, non-null = edit

        [Required(ErrorMessage = "Module name is required.")]
        [StringLength(100)]
        public string ModuleName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Duration is required.")]
        [Range(1, 1200, ErrorMessage = "Duration must be between 1 and 1200 months.")]
        public int RetentionDurationMonths { get; set; }

        [Required(ErrorMessage = "Auto action is required.")]
        public string AutoActionAfterExpiry { get; set; } = "ManualReview";

        public bool IsEnabled { get; set; } = true;
    }
}
