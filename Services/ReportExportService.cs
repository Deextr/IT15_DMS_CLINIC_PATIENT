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
        /// </summary>
        public byte[] GenerateExcel(string reportKey, object data)
        {
            using var workbook = new XLWorkbook();

            switch (reportKey)
            {
                case "All":
                    var all = (AllReportsExportData)data;
                    if (all.PatientSummary != null)
                        BuildPatientSummaryExcel(workbook, all.PatientSummary);
                    if (all.DocumentActivity != null)
                        BuildDocumentActivityExcel(workbook, all.DocumentActivity);
                    if (all.AuditLogs != null)
                        BuildAuditLogsExcel(workbook, all.AuditLogs);
                    break;
                case "PatientSummary":
                    BuildPatientSummaryExcel(workbook, (PatientSummaryReport)data);
                    break;
                case "DocumentActivity":
                    BuildDocumentActivityExcel(workbook, (DocumentActivityReport)data);
                    break;
                case "AuditLogs":
                    BuildAuditLogsExcel(workbook, (AuditLogsReportData)data);
                    break;
                default:
                    var ws = workbook.Worksheets.Add("Report");
                    ws.Cell(1, 1).Value = "No data available for this report.";
                    break;
            }

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        private void BuildPatientSummaryExcel(XLWorkbook wb, PatientSummaryReport rpt)
        {
            // Summary sheet
            var ws1 = wb.Worksheets.Add("Patient Summary");
            ws1.Cell(1, 1).Value = "Metric";
            ws1.Cell(1, 2).Value = "Value";
            StyleHeader(ws1, 1, 2);
            ws1.Cell(2, 1).Value = "Total Registered Patients"; ws1.Cell(2, 2).Value = rpt.TotalRegisteredPatients;
            ws1.Cell(3, 1).Value = "New Patients — Today"; ws1.Cell(3, 2).Value = rpt.NewPatientsDaily;
            ws1.Cell(4, 1).Value = "New Patients — Last 7 Days"; ws1.Cell(4, 2).Value = rpt.NewPatientsWeekly;
            ws1.Cell(5, 1).Value = "New Patients — Last 30 Days"; ws1.Cell(5, 2).Value = rpt.NewPatientsMonthly;
            ws1.Columns().AdjustToContents();

            // Age Distribution sheet
            var ws2 = wb.Worksheets.Add("Age Distribution");
            WriteExcelTable(ws2, rpt.AgeDistribution,
                new[] { "Age Group", "Count", "Percentage" },
                (r, row) => { ws2.Cell(row, 1).Value = r.AgeGroup; ws2.Cell(row, 2).Value = r.Count; ws2.Cell(row, 3).Value = r.Percentage; });

            // Gender Distribution sheet
            var ws3 = wb.Worksheets.Add("Gender Distribution");
            WriteExcelTable(ws3, rpt.GenderDistribution,
                new[] { "Gender", "Count", "Percentage" },
                (r, row) => { ws3.Cell(row, 1).Value = r.Gender; ws3.Cell(row, 2).Value = r.Count; ws3.Cell(row, 3).Value = r.Percentage; });

            // Recently Active Patients sheet
            var ws4 = wb.Worksheets.Add("Recently Active Patients");
            WriteExcelTable(ws4, rpt.RecentlyActivePatients,
                new[] { "ID", "Patient Name", "Gender", "Last Visit", "Documents" },
                (r, row) => { ws4.Cell(row, 1).Value = r.PatientID; ws4.Cell(row, 2).Value = r.PatientName; ws4.Cell(row, 3).Value = r.Gender; ws4.Cell(row, 4).Value = r.LastVisitDate; ws4.Cell(row, 5).Value = r.TotalDocuments; });
        }

        private void BuildDocumentActivityExcel(XLWorkbook wb, DocumentActivityReport rpt)
        {
            // Summary sheet
            var ws1 = wb.Worksheets.Add("Document Summary");
            ws1.Cell(1, 1).Value = "Metric";
            ws1.Cell(1, 2).Value = "Value";
            StyleHeader(ws1, 1, 2);
            ws1.Cell(2, 1).Value = "Total Documents Uploaded"; ws1.Cell(2, 2).Value = rpt.TotalDocumentsUploaded;
            ws1.Cell(3, 1).Value = "Documents Uploaded — Today"; ws1.Cell(3, 2).Value = rpt.DocumentsUploadedDaily;
            ws1.Cell(4, 1).Value = "Documents Uploaded — Last 7 Days"; ws1.Cell(4, 2).Value = rpt.DocumentsUploadedWeekly;
            ws1.Cell(5, 1).Value = "Documents Uploaded — Last 30 Days"; ws1.Cell(5, 2).Value = rpt.DocumentsUploadedMonthly;
            ws1.Columns().AdjustToContents();

            // Documents per Patient
            var ws2 = wb.Worksheets.Add("Documents per Patient");
            WriteExcelTable(ws2, rpt.DocumentsPerPatient,
                new[] { "ID", "Patient Name", "Documents", "Document Types" },
                (r, row) => { ws2.Cell(row, 1).Value = r.PatientID; ws2.Cell(row, 2).Value = r.PatientName; ws2.Cell(row, 3).Value = r.TotalDocuments; ws2.Cell(row, 4).Value = r.DocumentTypes; });

            // Documents per Category
            var ws3 = wb.Worksheets.Add("Documents per Category");
            WriteExcelTable(ws3, rpt.DocumentsPerCategory,
                new[] { "Document Type", "Count", "Percentage" },
                (r, row) => { ws3.Cell(row, 1).Value = r.DocumentType; ws3.Cell(row, 2).Value = r.DocumentCount; ws3.Cell(row, 3).Value = r.Percentage; });

            // Frequently Updated Documents
            var ws4 = wb.Worksheets.Add("Frequently Updated");
            WriteExcelTable(ws4, rpt.FrequentlyUpdatedDocuments,
                new[] { "ID", "Title", "Type", "Patient", "Versions", "Last Updated" },
                (r, row) => { ws4.Cell(row, 1).Value = r.DocumentID; ws4.Cell(row, 2).Value = r.DocumentTitle; ws4.Cell(row, 3).Value = r.DocumentType; ws4.Cell(row, 4).Value = r.PatientName; ws4.Cell(row, 5).Value = r.VersionCount; ws4.Cell(row, 6).Value = r.LastUpdated; });

            // Version Count
            var ws5 = wb.Worksheets.Add("Version Count");
            WriteExcelTable(ws5, rpt.VersionCounts,
                new[] { "ID", "Title", "Type", "Patient", "Versions" },
                (r, row) => { ws5.Cell(row, 1).Value = r.DocumentID; ws5.Cell(row, 2).Value = r.DocumentTitle; ws5.Cell(row, 3).Value = r.DocumentType; ws5.Cell(row, 4).Value = r.PatientName; ws5.Cell(row, 5).Value = r.VersionCount; });
        }

        private void BuildAuditLogsExcel(XLWorkbook wb, AuditLogsReportData rpt)
        {
            var ws = wb.Worksheets.Add("Audit Logs");
            WriteExcelTable(ws, rpt.Rows,
                new[] { "Log ID", "User", "Role", "Action", "Document", "Timestamp" },
                (r, row) => { ws.Cell(row, 1).Value = r.LogID; ws.Cell(row, 2).Value = r.UserFullName; ws.Cell(row, 3).Value = r.Role; ws.Cell(row, 4).Value = r.Action; ws.Cell(row, 5).Value = r.DocumentTitle; ws.Cell(row, 6).Value = r.Timestamp; });
        }

        private static void WriteExcelTable<T>(IXLWorksheet ws, List<T> rows, string[] headers, Action<T, int> writeRow)
        {
            for (int i = 0; i < headers.Length; i++)
                ws.Cell(1, i + 1).Value = headers[i];
            StyleHeader(ws, 1, headers.Length);

            for (int i = 0; i < rows.Count; i++)
                writeRow(rows[i], i + 2);

            ws.Columns().AdjustToContents();
        }

        private static void StyleHeader(IXLWorksheet ws, int row, int colCount)
        {
            var headerRange = ws.Range(row, 1, row, colCount);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#006d77");
            headerRange.Style.Font.FontColor = XLColor.White;
            headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        }

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
