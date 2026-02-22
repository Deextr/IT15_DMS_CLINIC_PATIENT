namespace DMS_CPMS.Models.Staff
{
    public class StaffDashboardViewModel
    {
        // KPI Cards
        public int TotalDocuments { get; set; }
        public int TotalArchived { get; set; }
        public int TotalVersions { get; set; }
        public int DocumentsUploadedThisMonth { get; set; }

        // Charts
        public List<MonthlyUploadDataPoint> MonthlyUploadTrend { get; set; } = new();
        public List<DocumentTypeDataPoint> DocumentTypeDistribution { get; set; } = new();
        public int ActiveDocumentCount { get; set; }
        public int ArchivedDocumentCount { get; set; }

        // Recent Activity
        public List<RecentActivityItem> RecentActivities { get; set; } = new();

        // User info
        public string StaffFirstName { get; set; } = string.Empty;
    }

    public class MonthlyUploadDataPoint
    {
        public string Label { get; set; } = string.Empty; // e.g. "Jan 2026"
        public int Count { get; set; }
    }

    public class DocumentTypeDataPoint
    {
        public string DocumentType { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public class RecentActivityItem
    {
        public string DocumentName { get; set; } = string.Empty;
        public string PatientName { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;   // Uploaded / Updated / Archived
        public DateTime Date { get; set; }
        public string PerformedBy { get; set; } = string.Empty;
    }

    // AJAX response model for date-range filtering
    public class StaffDashboardDataResponse
    {
        public int TotalDocuments { get; set; }
        public int TotalArchived { get; set; }
        public int TotalVersions { get; set; }
        public int DocumentsUploadedThisMonth { get; set; }

        public List<MonthlyUploadDataPoint> MonthlyUploadTrend { get; set; } = new();
        public string TrendTitle { get; set; } = "Monthly Document Upload Trend";
        public List<DocumentTypeDataPoint> DocumentTypeDistribution { get; set; } = new();
        public int ActiveDocumentCount { get; set; }
        public int ArchivedDocumentCount { get; set; }

        public List<RecentActivityItem> RecentActivities { get; set; } = new();
    }
}
