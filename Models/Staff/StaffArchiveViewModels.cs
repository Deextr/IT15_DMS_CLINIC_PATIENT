using System;
using System.Collections.Generic;

namespace DMS_CPMS.Models.Staff
{
    // ───────────────────── Staff Archive Index ─────────────────────
    public class StaffArchiveIndexViewModel
    {
        // Archived Documents
        public List<StaffArchivedDocumentViewModel> ArchivedDocuments { get; set; } = new();

        // Stats
        public int TotalDocuments { get; set; }
        public int ActiveRetentionCount { get; set; }
        public int ExpiredRetentionCount { get; set; }

        // Pagination
        public int PageNumber { get; set; } = 1;
        public int TotalPages { get; set; }

        // Filters
        public string? SearchTerm { get; set; }
        public string? StatusFilter { get; set; }
    }

    // ───────────────────── Archived Document display ─────────────────────
    public class StaffArchivedDocumentViewModel
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
}
