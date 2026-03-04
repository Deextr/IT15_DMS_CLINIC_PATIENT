using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ClosedXML.Excel;
using DMS_CPMS.Models.Reports;

namespace DMS_CPMS.Services
{
    public interface IReportExportService
    {
        byte[] GenerateCsv(string reportKey, object data);
        byte[] GenerateExcel(string reportKey, object data);
    }

    public class ReportExportService : IReportExportService
    {
        /// <summary>
        /// Generate a CSV byte array from report data.
        /// </summary>
        public byte[] GenerateCsv(string reportKey, object data)
        {
            var sb = new StringBuilder();

            switch (reportKey)
            {
                case "PatientSummary":
                    var ps = (PatientSummaryReport)data;
                    sb.AppendLine("=== PATIENT SUMMARY ===");
                    sb.AppendLine($"Total Registered Patients,{ps.TotalRegisteredPatients}");
                    sb.AppendLine($"New Patients (Today),{ps.NewPatientsDaily}");
                    sb.AppendLine($"New Patients (Last 7 Days),{ps.NewPatientsWeekly}");
                    sb.AppendLine($"New Patients (Last 30 Days),{ps.NewPatientsMonthly}");
                    sb.AppendLine();
                    sb.AppendLine("=== AGE DISTRIBUTION ===");
                    WriteCsv(sb, ps.AgeDistribution,
                        new[] { "Age Group", "Count", "Percentage" },
                        r => new[] { r.AgeGroup, r.Count.ToString(), r.Percentage });
                    sb.AppendLine();
                    sb.AppendLine("=== GENDER DISTRIBUTION ===");
                    WriteCsv(sb, ps.GenderDistribution,
                        new[] { "Gender", "Count", "Percentage" },
                        r => new[] { r.Gender, r.Count.ToString(), r.Percentage });
                    sb.AppendLine();
                    sb.AppendLine("=== RECENTLY ACTIVE PATIENTS ===");
                    WriteCsv(sb, ps.RecentlyActivePatients,
                        new[] { "Patient ID", "Patient Name", "Gender", "Last Visit Date", "Total Documents" },
                        r => new[] { r.PatientID.ToString(), r.PatientName, r.Gender, r.LastVisitDate, r.TotalDocuments.ToString() });
                    break;

                case "DocumentActivity":
                    var da = (DocumentActivityReport)data;
                    sb.AppendLine("=== DOCUMENT SUMMARY ===");
                    sb.AppendLine($"Total Documents Uploaded,{da.TotalDocumentsUploaded}");
                    sb.AppendLine($"Documents Uploaded (Today),{da.DocumentsUploadedDaily}");
                    sb.AppendLine($"Documents Uploaded (Last 7 Days),{da.DocumentsUploadedWeekly}");
                    sb.AppendLine($"Documents Uploaded (Last 30 Days),{da.DocumentsUploadedMonthly}");
                    sb.AppendLine();
                    sb.AppendLine("=== DOCUMENTS PER PATIENT ===");
                    WriteCsv(sb, da.DocumentsPerPatient,
                        new[] { "Patient ID", "Patient Name", "Total Documents", "Document Types" },
                        r => new[] { r.PatientID.ToString(), r.PatientName, r.TotalDocuments.ToString(), r.DocumentTypes });
                    sb.AppendLine();
                    sb.AppendLine("=== DOCUMENTS PER CATEGORY ===");
                    WriteCsv(sb, da.DocumentsPerCategory,
                        new[] { "Document Type", "Count", "Percentage" },
                        r => new[] { r.DocumentType, r.DocumentCount.ToString(), r.Percentage });
                    sb.AppendLine();
                    sb.AppendLine("=== MOST FREQUENTLY UPDATED DOCUMENTS ===");
                    WriteCsv(sb, da.FrequentlyUpdatedDocuments,
                        new[] { "Document ID", "Title", "Type", "Patient", "Versions", "Last Updated" },
                        r => new[] { r.DocumentID.ToString(), r.DocumentTitle, r.DocumentType, r.PatientName, r.VersionCount.ToString(), r.LastUpdated });
                    sb.AppendLine();
                    sb.AppendLine("=== VERSION COUNT PER DOCUMENT ===");
                    WriteCsv(sb, da.VersionCounts,
                        new[] { "Document ID", "Title", "Type", "Patient", "Versions" },
                        r => new[] { r.DocumentID.ToString(), r.DocumentTitle, r.DocumentType, r.PatientName, r.VersionCount.ToString() });
                    break;

                case "AuditLogs":
                    var al = (AuditLogsReportData)data;
                    WriteCsv(sb, al.Rows,
                        new[] { "Log ID", "User", "Role", "Action", "Document", "Timestamp" },
                        r => new[] { r.LogID.ToString(), r.UserFullName, r.Role, r.Action, r.DocumentTitle, r.Timestamp });
                    break;

                default:
                    sb.AppendLine("No data available for this report.");
                    break;
            }

            return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        }

        /// <summary>
        /// Generate an Excel (.xlsx) byte array from report data.
        /// Each report type is written as a single worksheet with clearly labelled
        /// sections separated by blank rows, so ALL data is immediately visible on
        /// one sheet — no hidden tabs to discover.
        /// </summary>
        public byte[] GenerateExcel(string reportKey, object data)
        {
            using var workbook = new XLWorkbook();

            switch (reportKey)
            {
                case "All":
                    var all = (AllReportsExportData)data;
                    if (all.PatientSummary != null)
                        BuildPatientSummarySheet(workbook, all.PatientSummary);
                    if (all.DocumentActivity != null)
                        BuildDocumentActivitySheet(workbook, all.DocumentActivity);
                    if (all.AuditLogs != null)
                        BuildAuditLogsSheet(workbook, all.AuditLogs);
                    break;

                case "PatientSummary":
                    BuildPatientSummarySheet(workbook, (PatientSummaryReport)data);
                    break;

                case "DocumentActivity":
                    BuildDocumentActivitySheet(workbook, (DocumentActivityReport)data);
                    break;

                case "AuditLogs":
                    BuildAuditLogsSheet(workbook, (AuditLogsReportData)data);
                    break;

                default:
                    var wsDefault = workbook.Worksheets.Add("Report");
                    wsDefault.Cell(1, 1).Value = "No data available for this report.";
                    break;
            }

            // Ensure the workbook has at least one sheet (ClosedXML requirement)
            if (!workbook.Worksheets.Any())
            {
                var wsFallback = workbook.Worksheets.Add("Report");
                wsFallback.Cell(1, 1).Value = "No data available.";
            }

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Per-report sheet builders — each writes ONE sheet with all sections
        // ─────────────────────────────────────────────────────────────────────

        private void BuildPatientSummarySheet(XLWorkbook wb, PatientSummaryReport rpt)
        {
            var ws = wb.Worksheets.Add("Patient Summary");
            int row = 1;

            // ── Overview ──
            row = WriteSheetSectionHeader(ws, row, "PATIENT SUMMARY OVERVIEW", 2);
            ws.Cell(row, 1).Value = "Metric";
            ws.Cell(row, 2).Value = "Value";
            StyleSectionHeader(ws, row, 2);
            row++;
            ws.Cell(row, 1).Value = "Total Registered Patients";   ws.Cell(row++, 2).Value = rpt.TotalRegisteredPatients;
            ws.Cell(row, 1).Value = "New Patients — Today";         ws.Cell(row++, 2).Value = rpt.NewPatientsDaily;
            ws.Cell(row, 1).Value = "New Patients — Last 7 Days";   ws.Cell(row++, 2).Value = rpt.NewPatientsWeekly;
            ws.Cell(row, 1).Value = "New Patients — Last 30 Days";  ws.Cell(row++, 2).Value = rpt.NewPatientsMonthly;
            row++; // blank separator

            // ── Age Distribution ──
            var ageHeaders = new[] { "Age Group", "Count", "Percentage" };
            row = WriteSheetSectionHeader(ws, row, "AGE DISTRIBUTION", ageHeaders.Length);
            WriteInlineHeaders(ws, row, ageHeaders); row++;
            foreach (var r in rpt.AgeDistribution ?? new())
            {
                ws.Cell(row, 1).Value = r.AgeGroup;
                ws.Cell(row, 2).Value = r.Count;
                ws.Cell(row, 3).Value = r.Percentage;
                row++;
            }
            if (!(rpt.AgeDistribution?.Any() ?? false)) { ws.Cell(row, 1).Value = "No data available."; row++; }
            row++; // blank separator

            // ── Gender Distribution ──
            var genderHeaders = new[] { "Gender", "Count", "Percentage" };
            row = WriteSheetSectionHeader(ws, row, "GENDER DISTRIBUTION", genderHeaders.Length);
            WriteInlineHeaders(ws, row, genderHeaders); row++;
            foreach (var r in rpt.GenderDistribution ?? new())
            {
                ws.Cell(row, 1).Value = r.Gender;
                ws.Cell(row, 2).Value = r.Count;
                ws.Cell(row, 3).Value = r.Percentage;
                row++;
            }
            if (!(rpt.GenderDistribution?.Any() ?? false)) { ws.Cell(row, 1).Value = "No data available."; row++; }
            row++; // blank separator

            // ── Recently Active Patients ──
            var rapHeaders = new[] { "Patient ID", "Patient Name", "Gender", "Last Visit Date", "Total Documents" };
            row = WriteSheetSectionHeader(ws, row, "RECENTLY ACTIVE PATIENTS", rapHeaders.Length);
            WriteInlineHeaders(ws, row, rapHeaders); row++;
            foreach (var r in rpt.RecentlyActivePatients ?? new())
            {
                ws.Cell(row, 1).Value = r.PatientID;
                ws.Cell(row, 2).Value = r.PatientName;
                ws.Cell(row, 3).Value = r.Gender;
                ws.Cell(row, 4).Value = r.LastVisitDate;
                ws.Cell(row, 5).Value = r.TotalDocuments;
                row++;
            }
            if (!(rpt.RecentlyActivePatients?.Any() ?? false)) { ws.Cell(row, 1).Value = "No data available."; row++; }

            ws.Columns().AdjustToContents();
        }

        private void BuildDocumentActivitySheet(XLWorkbook wb, DocumentActivityReport rpt)
        {
            var ws = wb.Worksheets.Add("Document Activity");
            int row = 1;

            // ── Overview ──
            row = WriteSheetSectionHeader(ws, row, "DOCUMENT ACTIVITY OVERVIEW", 2);
            ws.Cell(row, 1).Value = "Metric";
            ws.Cell(row, 2).Value = "Value";
            StyleSectionHeader(ws, row, 2);
            row++;
            ws.Cell(row, 1).Value = "Total Documents Uploaded";            ws.Cell(row++, 2).Value = rpt.TotalDocumentsUploaded;
            ws.Cell(row, 1).Value = "Documents Uploaded — Today";           ws.Cell(row++, 2).Value = rpt.DocumentsUploadedDaily;
            ws.Cell(row, 1).Value = "Documents Uploaded — Last 7 Days";     ws.Cell(row++, 2).Value = rpt.DocumentsUploadedWeekly;
            ws.Cell(row, 1).Value = "Documents Uploaded — Last 30 Days";    ws.Cell(row++, 2).Value = rpt.DocumentsUploadedMonthly;
            row++;

            // ── Documents per Patient ──
            var dppHeaders = new[] { "Patient ID", "Patient Name", "Total Documents", "Document Types" };
            row = WriteSheetSectionHeader(ws, row, "DOCUMENTS PER PATIENT", dppHeaders.Length);
            WriteInlineHeaders(ws, row, dppHeaders); row++;
            foreach (var r in rpt.DocumentsPerPatient ?? new())
            {
                ws.Cell(row, 1).Value = r.PatientID;
                ws.Cell(row, 2).Value = r.PatientName;
                ws.Cell(row, 3).Value = r.TotalDocuments;
                ws.Cell(row, 4).Value = r.DocumentTypes;
                row++;
            }
            if (!(rpt.DocumentsPerPatient?.Any() ?? false)) { ws.Cell(row, 1).Value = "No data available."; row++; }
            row++;

            // ── Documents per Category ──
            var dpcHeaders = new[] { "Document Type", "Count", "Percentage" };
            row = WriteSheetSectionHeader(ws, row, "DOCUMENTS PER CATEGORY / TYPE", dpcHeaders.Length);
            WriteInlineHeaders(ws, row, dpcHeaders); row++;
            foreach (var r in rpt.DocumentsPerCategory ?? new())
            {
                ws.Cell(row, 1).Value = r.DocumentType;
                ws.Cell(row, 2).Value = r.DocumentCount;
                ws.Cell(row, 3).Value = r.Percentage;
                row++;
            }
            if (!(rpt.DocumentsPerCategory?.Any() ?? false)) { ws.Cell(row, 1).Value = "No data available."; row++; }
            row++;

            // ── Most Frequently Updated Documents ──
            var fuHeaders = new[] { "Document ID", "Document Title", "Type", "Patient", "Versions", "Last Updated" };
            row = WriteSheetSectionHeader(ws, row, "MOST FREQUENTLY UPDATED DOCUMENTS", fuHeaders.Length);
            WriteInlineHeaders(ws, row, fuHeaders); row++;
            foreach (var r in rpt.FrequentlyUpdatedDocuments ?? new())
            {
                ws.Cell(row, 1).Value = r.DocumentID;
                ws.Cell(row, 2).Value = r.DocumentTitle;
                ws.Cell(row, 3).Value = r.DocumentType;
                ws.Cell(row, 4).Value = r.PatientName;
                ws.Cell(row, 5).Value = r.VersionCount;
                ws.Cell(row, 6).Value = r.LastUpdated;
                row++;
            }
            if (!(rpt.FrequentlyUpdatedDocuments?.Any() ?? false)) { ws.Cell(row, 1).Value = "No data available."; row++; }
            row++;

            // ── Version Count per Document ──
            var vcHeaders = new[] { "Document ID", "Document Title", "Type", "Patient", "Versions" };
            row = WriteSheetSectionHeader(ws, row, "VERSION COUNT PER DOCUMENT", vcHeaders.Length);
            WriteInlineHeaders(ws, row, vcHeaders); row++;
            foreach (var r in rpt.VersionCounts ?? new())
            {
                ws.Cell(row, 1).Value = r.DocumentID;
                ws.Cell(row, 2).Value = r.DocumentTitle;
                ws.Cell(row, 3).Value = r.DocumentType;
                ws.Cell(row, 4).Value = r.PatientName;
                ws.Cell(row, 5).Value = r.VersionCount;
                row++;
            }
            if (!(rpt.VersionCounts?.Any() ?? false)) { ws.Cell(row, 1).Value = "No data available."; row++; }

            ws.Columns().AdjustToContents();
        }

        private void BuildAuditLogsSheet(XLWorkbook wb, AuditLogsReportData rpt)
        {
            var ws = wb.Worksheets.Add("Audit Logs");
            int row = 1;

            var headers = new[] { "Log ID", "User", "Role", "Action", "Document", "Timestamp" };
            row = WriteSheetSectionHeader(ws, row, "AUDIT LOG ENTRIES", headers.Length);
            WriteInlineHeaders(ws, row, headers); row++;
            foreach (var r in rpt.Rows ?? new())
            {
                ws.Cell(row, 1).Value = r.LogID;
                ws.Cell(row, 2).Value = r.UserFullName;
                ws.Cell(row, 3).Value = r.Role;
                ws.Cell(row, 4).Value = r.Action;
                ws.Cell(row, 5).Value = r.DocumentTitle;
                ws.Cell(row, 6).Value = r.Timestamp;
                row++;
            }
            if (!(rpt.Rows?.Any() ?? false)) { ws.Cell(row, 1).Value = "No data available."; }

            ws.Columns().AdjustToContents();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Writes a full-width merged section title banner and returns the next row index.
        /// </summary>
        private static int WriteSheetSectionHeader(IXLWorksheet ws, int row, string title, int colSpan)
        {
            // Merge cells across the data width for the section banner
            var mergeRange = ws.Range(row, 1, row, Math.Max(colSpan, 1));
            mergeRange.Merge();
            mergeRange.FirstCell().Value = title;
            mergeRange.Style.Font.Bold = true;
            mergeRange.Style.Font.FontSize = 11;
            mergeRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#004e57");
            mergeRange.Style.Font.FontColor = XLColor.White;
            mergeRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            mergeRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            return row + 1;
        }

        /// <summary>Writes column header row with teal background.</summary>
        private static void WriteInlineHeaders(IXLWorksheet ws, int row, string[] headers)
        {
            for (int i = 0; i < headers.Length; i++)
                ws.Cell(row, i + 1).Value = headers[i];
            StyleSectionHeader(ws, row, headers.Length);
        }

        private static void StyleSectionHeader(IXLWorksheet ws, int row, int colCount)
        {
            var headerRange = ws.Range(row, 1, row, colCount);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#006d77");
            headerRange.Style.Font.FontColor = XLColor.White;
            headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        }

        // Keep legacy alias so existing callers compile without change
        private static void StyleHeader(IXLWorksheet ws, int row, int colCount)
            => StyleSectionHeader(ws, row, colCount);

        private static void WriteCsv<T>(StringBuilder sb, List<T> rows, string[] headers, Func<T, string[]> rowSelector)
        {
            sb.AppendLine(string.Join(",", headers.Select(EscapeCsv)));
            foreach (var row in rows)
            {
                sb.AppendLine(string.Join(",", rowSelector(row).Select(EscapeCsv)));
            }
        }

        private static string EscapeCsv(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "\"\"";
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
                return $"\"{value.Replace("\"", "\"\"")}\"";
            return value;
        }
    }
}
