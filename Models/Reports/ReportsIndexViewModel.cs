using System;
using System.Collections.Generic;

namespace DMS_CPMS.Models.Reports
{
    /// <summary>
    /// Master ViewModel for the restructured Reports Module.
    /// Three report categories: PatientSummary, DocumentActivity, AuditLogs.
    /// </summary>
    public class ReportsIndexViewModel
    {
        // Filter state — "PatientSummary", "DocumentActivity", "AuditLogs"
        public string SelectedReport { get; set; } = "PatientSummary";
        public string DatePreset { get; set; } = "AllTime";
        public string? DateFrom { get; set; }
        public string? DateTo { get; set; }
        public string Area { get; set; } = "SuperAdmin";

        // The three structured report categories
        public PatientSummaryReport? PatientSummary { get; set; }
        public DocumentActivityReport? DocumentActivity { get; set; }
        public AuditLogsReportData? AuditLogs { get; set; }
    }

    // ════════════════════════════════════════════════════════════════
    //  1. PATIENT SUMMARY REPORT
    // ════════════════════════════════════════════════════════════════

    public class PatientSummaryReport
    {
        // ── Summary Metrics ──
        public int TotalRegisteredPatients { get; set; }
        public int NewPatientsDaily { get; set; }
        public int NewPatientsWeekly { get; set; }
        public int NewPatientsMonthly { get; set; }

        // ── Demographics ──
        public List<AgeDistributionRow> AgeDistribution { get; set; } = new();
        public List<GenderDistributionRow> GenderDistribution { get; set; } = new();

        // ── Recently Active Patients ──
        public List<RecentlyActivePatientRow> RecentlyActivePatients { get; set; } = new();
    }

    public class AgeDistributionRow
    {
        public string AgeGroup { get; set; } = string.Empty;
        public int Count { get; set; }
        public string Percentage { get; set; } = string.Empty;
    }

    public class GenderDistributionRow
    {
        public string Gender { get; set; } = string.Empty;
        public int Count { get; set; }
        public string Percentage { get; set; } = string.Empty;
    }

    public class RecentlyActivePatientRow
    {
        public int PatientID { get; set; }
        public string PatientName { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public string LastVisitDate { get; set; } = string.Empty;
        public int TotalDocuments { get; set; }
    }

    // ════════════════════════════════════════════════════════════════
    //  2. DOCUMENT ACTIVITY REPORT
    // ════════════════════════════════════════════════════════════════

    public class DocumentActivityReport
    {
        // ── Summary Metrics ──
        public int TotalDocumentsUploaded { get; set; }
        public int DocumentsUploadedDaily { get; set; }
        public int DocumentsUploadedWeekly { get; set; }
        public int DocumentsUploadedMonthly { get; set; }

        // ── Documents per Patient ──
        public List<DocumentsPerPatientRow> DocumentsPerPatient { get; set; } = new();

        // ── Documents per Category/Type ──
        public List<DocumentsPerCategoryRow> DocumentsPerCategory { get; set; } = new();

        // ── Most Frequently Updated Documents ──
        public List<FrequentlyUpdatedDocumentRow> FrequentlyUpdatedDocuments { get; set; } = new();

        // ── Version Count per Document ──
        public List<VersionCountRow> VersionCounts { get; set; } = new();

        // Pagination (for Documents per Patient table)
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 8;
        public int TotalPages { get; set; }
        public int TotalCount { get; set; }
    }

    public class DocumentsPerPatientRow
    {
        public int PatientID { get; set; }
        public string PatientName { get; set; } = string.Empty;
        public int TotalDocuments { get; set; }
        public string DocumentTypes { get; set; } = string.Empty;
    }

    public class DocumentsPerCategoryRow
    {
        public string DocumentType { get; set; } = string.Empty;
        public int DocumentCount { get; set; }
        public string Percentage { get; set; } = string.Empty;
    }

    public class FrequentlyUpdatedDocumentRow
    {
        public int DocumentID { get; set; }
        public string DocumentTitle { get; set; } = string.Empty;
        public string DocumentType { get; set; } = string.Empty;
        public string PatientName { get; set; } = string.Empty;
        public int VersionCount { get; set; }
        public string LastUpdated { get; set; } = string.Empty;
    }

    public class VersionCountRow
    {
        public int DocumentID { get; set; }
        public string DocumentTitle { get; set; } = string.Empty;
        public string DocumentType { get; set; } = string.Empty;
        public string PatientName { get; set; } = string.Empty;
        public int VersionCount { get; set; }
    }

    // ════════════════════════════════════════════════════════════════
    //  3. AUDIT LOGS REPORT
    // ════════════════════════════════════════════════════════════════

    public class AuditLogsReportData
    {
        public List<AuditLogReportRow> Rows { get; set; } = new();
        public int TotalEntries { get; set; }

        // Pagination
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 8;
        public int TotalPages { get; set; }
        public int TotalCount { get; set; }
    }

    public class AuditLogReportRow
    {
        public int LogID { get; set; }
        public string UserFullName { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string DocumentTitle { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
    }

    // ════════════════════════════════════════════════════════════════
    //  ALL REPORTS EXPORT DATA (combined for "All" export)
    // ════════════════════════════════════════════════════════════════

    public class AllReportsExportData
    {
        public PatientSummaryReport? PatientSummary { get; set; }
        public DocumentActivityReport? DocumentActivity { get; set; }
        public AuditLogsReportData? AuditLogs { get; set; }
    }

    // ════════════════════════════════════════════════════════════════
    //  PDF EXPORT DATA (generic table structure for jsPDF)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Represents a single table section in the PDF export.
    /// </summary>
    public class PdfExportSection
    {
        /// <summary>Report group title (e.g. "User / Account Activity Report")</summary>
        public string ReportTitle { get; set; } = string.Empty;

        /// <summary>Sub-section title (e.g. "Login Activity")</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>Column headers for this table.</summary>
        public List<string> Headers { get; set; } = new();

        /// <summary>All data rows — never paginated, always the full filtered set.</summary>
        public List<List<string>> Rows { get; set; } = new();
    }

    /// <summary>
    /// Top-level payload returned by the ExportPdfData endpoint.
    /// Contains all sections (tables) to render in the PDF.
    /// </summary>
    public class PdfExportData
    {
        public List<PdfExportSection> Sections { get; set; } = new();
    }
}
