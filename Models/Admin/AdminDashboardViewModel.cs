namespace DMS_CPMS.Models.Admin
{
    // ── Initial page-load model ──
    public class AdminDashboardViewModel
    {
        // KPI Cards
        public int TotalPatients { get; set; }
        public int NewPatients { get; set; }
        public int TotalDocuments { get; set; }
        public int TotalArchiveDocuments { get; set; }
        public int ActiveStaff { get; set; }

        // Patient Charts
        public List<ChartDataPoint> PatientsByGender { get; set; } = new();
        public List<ChartDataPoint> PatientsByAgeGroup { get; set; } = new();
        public List<ChartDataPoint> NewPatientsPerMonth { get; set; } = new();

        // Document Analytics
        public List<ChartDataPoint> DocumentUploadTrend { get; set; } = new();
        public string DocumentTrendTitle { get; set; } = "Documents Uploaded Per Month";
        public List<ChartDataPoint> DocumentsByType { get; set; } = new();
        public List<MostUpdatedDocItem> MostUpdatedDocuments { get; set; } = new();
        public int TotalDocumentVersions { get; set; }

        // Recent Activity
        public List<DashboardActivityItem> RecentActivities { get; set; } = new();
    }

    // ── AJAX date-filter response ──
    public class AdminDashboardDataResponse
    {
        // KPI Cards
        public int TotalPatients { get; set; }
        public int NewPatients { get; set; }
        public int TotalDocuments { get; set; }
        public int TotalArchiveDocuments { get; set; }
        public int ActiveStaff { get; set; }

        // Patient Charts
        public List<ChartDataPoint> PatientsByGender { get; set; } = new();
        public List<ChartDataPoint> PatientsByAgeGroup { get; set; } = new();
        public List<ChartDataPoint> NewPatientsPerMonth { get; set; } = new();

        // Document Analytics
        public List<ChartDataPoint> DocumentUploadTrend { get; set; } = new();
        public string DocumentTrendTitle { get; set; } = "Documents Uploaded Per Month";
        public List<ChartDataPoint> DocumentsByType { get; set; } = new();
        public List<MostUpdatedDocItem> MostUpdatedDocuments { get; set; } = new();
        public int TotalDocumentVersions { get; set; }

        // Recent Activity
        public List<DashboardActivityItem> RecentActivities { get; set; } = new();
    }

    // ── Shared data-point for all charts ──
    public class ChartDataPoint
    {
        public string Label { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    // ── Most-updated documents list item ──
    public class MostUpdatedDocItem
    {
        public int DocumentID { get; set; }
        public string DocumentTitle { get; set; } = string.Empty;
        public string PatientName { get; set; } = string.Empty;
        public int VersionCount { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    // ── Recent activity item ──
    public class DashboardActivityItem
    {
        public string Description { get; set; } = string.Empty;
        public string ActionType { get; set; } = string.Empty; // Upload, Update, Create, Archive, etc.
        public DateTime Timestamp { get; set; }
        public string TimeAgo { get; set; } = string.Empty;
    }
}
