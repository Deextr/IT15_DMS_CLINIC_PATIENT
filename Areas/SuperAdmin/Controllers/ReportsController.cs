using System;
using System.Linq;
using System.Threading.Tasks;
using DMS_CPMS.Models.Reports;
using DMS_CPMS.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DMS_CPMS.Areas.SuperAdmin.Controllers
{
    [Area("SuperAdmin")]
    [Authorize(Roles = "SuperAdmin")]
    public class ReportsController : Controller
    {
        private readonly IReportService _reportService;
        private readonly IReportExportService _exportService;

        private static readonly string[] ValidReports =
            { "All", "PatientSummary", "DocumentActivity", "AuditLogs" };

        public ReportsController(IReportService reportService, IReportExportService exportService)
        {
            _reportService = reportService;
            _exportService = exportService;
        }

        [HttpGet]
        public async Task<IActionResult> Index(
            string? report,
            string? datePreset,
            string? dateFrom,
            string? dateTo,
            int page = 1)
        {
            var selected = !string.IsNullOrWhiteSpace(report) && ValidReports.Contains(report)
                ? report : "All";

            var preset = !string.IsNullOrWhiteSpace(datePreset) ? datePreset : "AllTime";
            ComputeDateRange(preset, dateFrom, dateTo, out var from, out var to);

            var model = new ReportsIndexViewModel
            {
                SelectedReport = selected,
                DatePreset = preset,
                DateFrom = from?.ToString("yyyy-MM-dd"),
                DateTo = to?.ToString("yyyy-MM-dd"),
                Area = "SuperAdmin"
            };

            if (selected == "All" || selected == "PatientSummary")
                model.PatientSummary = await _reportService.GetPatientSummaryAsync(from, to);
            if (selected == "All" || selected == "DocumentActivity")
                model.DocumentActivity = await _reportService.GetDocumentActivityAsync(from, to, page);
            if (selected == "All" || selected == "AuditLogs")
                model.AuditLogs = await _reportService.GetAuditLogsAsync(from, to, page);

            return View("~/Views/SuperAdmin/Reports/Index.cshtml", model);
        }

        [HttpGet]
        public async Task<IActionResult> ExportCsv(string report, string? datePreset, string? dateFrom, string? dateTo)
        {
            var preset = datePreset ?? "AllTime";
            ComputeDateRange(preset, dateFrom, dateTo, out var from, out var to);

            var data = await GetAllDataForReport(report, from, to);
            if (data == null) return NotFound();

            var csvBytes = _exportService.GenerateCsv(report, data);
            return File(csvBytes, "text/csv", $"{report}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        }

        [HttpGet]
        public async Task<IActionResult> ExportExcel(string report, string? datePreset, string? dateFrom, string? dateTo)
        {
            var preset = datePreset ?? "AllTime";
            ComputeDateRange(preset, dateFrom, dateTo, out var from, out var to);

            if (report == "All")
            {
                var allData = new AllReportsExportData
                {
                    PatientSummary = await _reportService.GetAllPatientSummaryDataAsync(from, to),
                    DocumentActivity = await _reportService.GetAllDocumentActivityDataAsync(from, to),
                    AuditLogs = await _reportService.GetAllAuditLogsDataAsync(from, to)
                };
                var excelBytes = _exportService.GenerateExcel("All", allData);
                return File(excelBytes,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"AllReports_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
            }

            var data = await GetAllDataForReport(report, from, to);
            if (data == null) return NotFound();

            var bytes = _exportService.GenerateExcel(report, data);
            return File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"{report}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
        }

        private static void ComputeDateRange(string preset, string? dateFromStr, string? dateToStr,
            out DateTime? from, out DateTime? to)
        {
            from = null;
            to = null;
            var today = DateTime.Today;

            switch (preset)
            {
                case "Today":
                    from = today;
                    to = today;
                    break;
                case "Weekly":
                    from = today.AddDays(-6);
                    to = today;
                    break;
                case "Monthly":
                    from = today.AddMonths(-1).AddDays(1);
                    to = today;
                    break;
                case "Yearly":
                    from = today.AddYears(-1).AddDays(1);
                    to = today;
                    break;
                case "Custom":
                    from = DateTime.TryParse(dateFromStr, out var f) ? f : null;
                    to = DateTime.TryParse(dateToStr, out var t) ? t : null;
                    break;
            }
        }

        private async Task<object?> GetAllDataForReport(string report, DateTime? from, DateTime? to)
        {
            return report switch
            {
                "PatientSummary" => await _reportService.GetAllPatientSummaryDataAsync(from, to),
                "DocumentActivity" => await _reportService.GetAllDocumentActivityDataAsync(from, to),
                "AuditLogs" => await _reportService.GetAllAuditLogsDataAsync(from, to),
                _ => null
            };
        }

        // ─────────────────────────────────────────────────────────
        //  PDF EXPORT — returns full unpaginated data as JSON
        // ─────────────────────────────────────────────────────────

        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> ExportPdfData(
            string report,
            string? datePreset,
            string? dateFrom,
            string? dateTo)
        {
            var preset = datePreset ?? "AllTime";
            ComputeDateRange(preset, dateFrom, dateTo, out var from, out var to);

            var result = new PdfExportData();

            var reportKeys = report == "All"
                ? new[] { "PatientSummary", "DocumentActivity", "AuditLogs" }
                : new[] { report };

            foreach (var key in reportKeys)
            {
                switch (key)
                {
                    case "PatientSummary":
                        var ps = await _reportService.GetAllPatientSummaryDataAsync(from, to);
                        result.Sections.AddRange(BuildPatientSummaryPdfSections(ps));
                        break;
                    case "DocumentActivity":
                        var da = await _reportService.GetAllDocumentActivityDataAsync(from, to);
                        result.Sections.AddRange(BuildDocumentActivityPdfSections(da));
                        break;
                    case "AuditLogs":
                        var al = await _reportService.GetAllAuditLogsDataAsync(from, to);
                        result.Sections.AddRange(BuildAuditLogsPdfSections(al));
                        break;
                }
            }

            return Json(result);
        }

        private static List<PdfExportSection> BuildPatientSummaryPdfSections(PatientSummaryReport ps)
        {
            return new List<PdfExportSection>
            {
                new PdfExportSection
                {
                    ReportTitle = "Patient Summary Report",
                    Title = "Patient Overview",
                    Headers = new List<string> { "Metric", "Value" },
                    Rows = new List<List<string>>
                    {
                        new() { "Total Registered Patients", ps.TotalRegisteredPatients.ToString() },
                        new() { "New Patients — Today", ps.NewPatientsDaily.ToString() },
                        new() { "New Patients — Last 7 Days", ps.NewPatientsWeekly.ToString() },
                        new() { "New Patients — Last 30 Days", ps.NewPatientsMonthly.ToString() }
                    }
                },
                new PdfExportSection
                {
                    ReportTitle = "Patient Summary Report",
                    Title = "Age Distribution",
                    Headers = new List<string> { "Age Group", "Count", "Percentage" },
                    Rows = ps.AgeDistribution
                        .Select(r => new List<string> { r.AgeGroup, r.Count.ToString(), r.Percentage })
                        .ToList()
                },
                new PdfExportSection
                {
                    ReportTitle = "Patient Summary Report",
                    Title = "Gender Distribution",
                    Headers = new List<string> { "Gender", "Count", "Percentage" },
                    Rows = ps.GenderDistribution
                        .Select(r => new List<string> { r.Gender, r.Count.ToString(), r.Percentage })
                        .ToList()
                },
                new PdfExportSection
                {
                    ReportTitle = "Patient Summary Report",
                    Title = "Recently Active Patients",
                    Headers = new List<string> { "Patient ID", "Patient Name", "Gender", "Last Visit Date", "Total Documents" },
                    Rows = ps.RecentlyActivePatients
                        .Select(r => new List<string> { r.PatientID.ToString(), r.PatientName, r.Gender, r.LastVisitDate, r.TotalDocuments.ToString() })
                        .ToList()
                }
            };
        }

        private static List<PdfExportSection> BuildDocumentActivityPdfSections(DocumentActivityReport da)
        {
            return new List<PdfExportSection>
            {
                new PdfExportSection
                {
                    ReportTitle = "Document Activity Report",
                    Title = "Document Overview",
                    Headers = new List<string> { "Metric", "Value" },
                    Rows = new List<List<string>>
                    {
                        new() { "Total Documents Uploaded", da.TotalDocumentsUploaded.ToString() },
                        new() { "Documents Uploaded — Today", da.DocumentsUploadedDaily.ToString() },
                        new() { "Documents Uploaded — Last 7 Days", da.DocumentsUploadedWeekly.ToString() },
                        new() { "Documents Uploaded — Last 30 Days", da.DocumentsUploadedMonthly.ToString() }
                    }
                },
                new PdfExportSection
                {
                    ReportTitle = "Document Activity Report",
                    Title = "Documents per Patient",
                    Headers = new List<string> { "Patient ID", "Patient Name", "Total Documents", "Document Types" },
                    Rows = da.DocumentsPerPatient
                        .Select(r => new List<string> { r.PatientID.ToString(), r.PatientName, r.TotalDocuments.ToString(), r.DocumentTypes })
                        .ToList()
                },
                new PdfExportSection
                {
                    ReportTitle = "Document Activity Report",
                    Title = "Documents per Category / Type",
                    Headers = new List<string> { "Document Type", "Count", "Percentage" },
                    Rows = da.DocumentsPerCategory
                        .Select(r => new List<string> { r.DocumentType, r.DocumentCount.ToString(), r.Percentage })
                        .ToList()
                },
                new PdfExportSection
                {
                    ReportTitle = "Document Activity Report",
                    Title = "Most Frequently Updated Documents",
                    Headers = new List<string> { "ID", "Document Title", "Type", "Patient", "Versions", "Last Updated" },
                    Rows = da.FrequentlyUpdatedDocuments
                        .Select(r => new List<string> { r.DocumentID.ToString(), r.DocumentTitle, r.DocumentType, r.PatientName, r.VersionCount.ToString(), r.LastUpdated })
                        .ToList()
                },
                new PdfExportSection
                {
                    ReportTitle = "Document Activity Report",
                    Title = "Version Count per Document",
                    Headers = new List<string> { "ID", "Document Title", "Type", "Patient", "Versions" },
                    Rows = da.VersionCounts
                        .Select(r => new List<string> { r.DocumentID.ToString(), r.DocumentTitle, r.DocumentType, r.PatientName, r.VersionCount.ToString() })
                        .ToList()
                }
            };
        }

        private static List<PdfExportSection> BuildAuditLogsPdfSections(AuditLogsReportData al)
        {
            return new List<PdfExportSection>
            {
                new PdfExportSection
                {
                    ReportTitle = "Audit Logs Report",
                    Title = "Audit Log Entries",
                    Headers = new List<string> { "Log ID", "User", "Role", "Action", "Document", "Timestamp" },
                    Rows = al.Rows
                        .Select(r => new List<string> { r.LogID.ToString(), r.UserFullName, r.Role, r.Action, r.DocumentTitle, r.Timestamp })
                        .ToList()
                }
            };
        }
    }
}
