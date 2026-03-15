namespace DMS_CPMS.Models.Staff
{
    public class StaffDashboardViewModel
    {
        // User info
        public string StaffFirstName { get; set; } = string.Empty;

        // Simple KPI Cards
        public int DocumentsUploadedToday { get; set; }
        public int DocumentsUploadedThisWeek { get; set; }
        public int TotalDocumentsHandled { get; set; }
        public int RecentlyArchivedCount { get; set; }

        // Recent Activity (human-readable)
        public List<RecentActivityItem> RecentActivities { get; set; } = new();

        // Notifications / Alerts
        public List<StaffNotificationItem> Notifications { get; set; } = new();
    }

    public class RecentActivityItem
    {
        public string DocumentName { get; set; } = string.Empty;
        public string PatientName { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public string PerformedBy { get; set; } = string.Empty;
        public string HumanMessage { get; set; } = string.Empty;
        public string RelativeTime { get; set; } = string.Empty;
    }

    public class StaffNotificationItem
    {
        public string Message { get; set; } = string.Empty;
        public string Type { get; set; } = "info"; // info, warning, danger
        public string Icon { get; set; } = "info";  // info, clock, alert
        public DateTime Date { get; set; }
    }

    // Keep these for Admin dashboard compatibility
    public class MonthlyUploadDataPoint
    {
        public string Label { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public class DocumentTypeDataPoint
    {
        public string DocumentType { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    // AJAX response model for date-range filtering (kept for admin compatibility)
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
