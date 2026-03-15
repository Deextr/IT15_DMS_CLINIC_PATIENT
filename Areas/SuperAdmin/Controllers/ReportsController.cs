using System;
using System.Linq;
using System.Threading.Tasks;
using DMS_CPMS.Models.Reports;
using DMS_CPMS.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace DMS_CPMS.Areas.SuperAdmin.Controllers
{
    [Area("SuperAdmin")]
    [Authorize(Roles = "SuperAdmin")]
    public class ReportsController : Controller
    {
        private readonly IReportService _reportService;
        private readonly IReportExportService _exportService;
        private readonly IS3ExportStorageService _s3Storage;
        private readonly ILogger<ReportsController> _logger;

        private static readonly string[] ValidReports =
            { "All", "PatientSummary", "DocumentActivity", "AuditLogs" };

        public ReportsController(
            IReportService reportService,
            IReportExportService exportService,
            IS3ExportStorageService s3Storage,
            ILogger<ReportsController> logger)
        {
            _reportService = reportService;
            _exportService = exportService;
            _s3Storage = s3Storage;
            _logger = logger;
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

            byte[] excelBytes;
            string reportName;

            if (report == "All")
            {
                var allData = new AllReportsExportData
                {
                    PatientSummary = await _reportService.GetAllPatientSummaryDataAsync(from, to),
                    DocumentActivity = await _reportService.GetAllDocumentActivityDataAsync(from, to),
                    AuditLogs = await _reportService.GetAllAuditLogsDataAsync(from, to)
                };
                excelBytes = _exportService.GenerateExcel("All", allData);
                reportName = "AllReports";
            }
            else
            {
                var data = await GetAllDataForReport(report, from, to);
                if (data == null) return NotFound();
                excelBytes = _exportService.GenerateExcel(report, data);
                reportName = report;
            }

            // Upload to S3 (fire-and-forget style with error handling — does not block download)
            _ = UploadToS3Async(excelBytes, reportName, ".xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

            return File(excelBytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"{reportName}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
        }

        // ─────────────────────────────────────────────────────────
        //  PDF EXPORT — server-side generation + S3 upload
        // ─────────────────────────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> ExportPdf(string report, string? datePreset, string? dateFrom, string? dateTo)
        {
            var preset = datePreset ?? "AllTime";
            ComputeDateRange(preset, dateFrom, dateTo, out var from, out var to);

            byte[] pdfBytes;
            string reportName;

            if (report == "All")
            {
                var allData = new AllReportsExportData
                {
                    PatientSummary = await _reportService.GetAllPatientSummaryDataAsync(from, to),
                    DocumentActivity = await _reportService.GetAllDocumentActivityDataAsync(from, to),
                    AuditLogs = await _reportService.GetAllAuditLogsDataAsync(from, to)
                };
                pdfBytes = _exportService.GeneratePdf("All", allData);
                reportName = "AllReports";
            }
            else
            {
                var data = await GetAllDataForReport(report, from, to);
                if (data == null) return NotFound();
                pdfBytes = _exportService.GeneratePdf(report, data);
                reportName = report;
            }

            // Upload to S3
            _ = UploadToS3Async(pdfBytes, reportName, ".pdf", "application/pdf");

            return File(pdfBytes, "application/pdf",
                $"{reportName}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
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
        //  PDF DATA (JSON) — kept for backward compatibility with
        //  the client-side jsPDF fallback if needed
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
                        result.Sections.AddRange(ReportExportService.BuildPatientSummaryPdfSections(ps));
                        break;
                    case "DocumentActivity":
                        var da = await _reportService.GetAllDocumentActivityDataAsync(from, to);
                        result.Sections.AddRange(ReportExportService.BuildDocumentActivityPdfSections(da));
                        break;
                    case "AuditLogs":
                        var al = await _reportService.GetAllAuditLogsDataAsync(from, to);
                        result.Sections.AddRange(ReportExportService.BuildAuditLogsPdfSections(al));
                        break;
                }
            }

            return Json(result);
        }

        // ─────────────────────────────────────────────────────────
        //  Helper: Upload to S3 (non-blocking)
        // ─────────────────────────────────────────────────────────

        private async Task UploadToS3Async(byte[] fileBytes, string reportName, string extension, string contentType)
        {
            try
            {
                var objectKey = await _s3Storage.UploadReportAsync(fileBytes, reportName, extension, contentType);
                _logger.LogInformation("Report uploaded to S3: {ObjectKey}", objectKey);
            }
            catch (Exception ex)
            {
                // Log but do NOT throw — the user still gets the download even if S3 fails
                _logger.LogError(ex, "Failed to upload {ReportName}{Extension} to S3. The user download was not affected.", reportName, extension);
            }
        }
    }
}
