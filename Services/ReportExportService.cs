using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ClosedXML.Excel;
using DMS_CPMS.Models.Reports;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace DMS_CPMS.Services
{
    public interface IReportExportService
    {
        byte[] GenerateCsv(string reportKey, object data);
        byte[] GenerateExcel(string reportKey, object data);
        byte[] GeneratePdf(string reportKey, object data);
    }

    public class ReportExportService : IReportExportService
    {
        // ─────────────────────────────────────────────────────────────────────
        //  Server-side PDF generation using QuestPDF
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Generate a PDF byte array from report data using QuestPDF.
        /// Produces a landscape-A4 document with styled tables matching the
        /// existing jsPDF client-side output.
        /// </summary>
        public byte[] GeneratePdf(string reportKey, object data)
        {
            // Build the list of sections (same structure as the PdfExportData JSON)
            var sections = BuildPdfSections(reportKey, data);

            var document = Document.Create(container =>
            {
                foreach (var section in sections)
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4.Landscape());
                        page.Margin(30);
                        page.DefaultTextStyle(x => x.FontSize(8));

                        // Header
                        page.Header().Column(col =>
                        {
                            col.Item().Row(row =>
                            {
                                row.RelativeItem().Text($"DMS CPMS \u2014 {section.ReportTitle}")
                                    .FontSize(14).Bold().FontColor("#006d77");
                                row.ConstantItem(180).AlignRight()
                                    .Text($"Generated: {DateTime.Now:MMMM dd, yyyy hh:mm tt}")
                                    .FontSize(7).FontColor("#828282");
                            });
                            col.Item().Text(section.Title).FontSize(9).FontColor("#505050");
                            col.Item().PaddingVertical(4).LineHorizontal(0.5f).LineColor("#dddddd");
                        });

                        // Content — table
                        page.Content().PaddingVertical(5).Table(table =>
                        {
                            var colCount = section.Headers.Count;
                            table.ColumnsDefinition(cols =>
                            {
                                for (int i = 0; i < colCount; i++)
                                    cols.RelativeColumn();
                            });

                            // Header row
                            foreach (var header in section.Headers)
                            {
                                table.Cell()
                                    .Background("#006d77")
                                    .Padding(4)
                                    .Text(header)
                                    .FontSize(8).Bold().FontColor(Colors.White);
                            }

                            // Data rows
                            if (section.Rows.Count == 0)
                            {
                                table.Cell().ColumnSpan((uint)colCount)
                                    .Padding(4)
                                    .Text("No data available.").Italic().FontColor("#999999");
                            }
                            else
                            {
                                for (int r = 0; r < section.Rows.Count; r++)
                                {
                                    var bgColor = r % 2 == 1 ? "#f5f8fa" : "#ffffff";
                                    foreach (var cell in section.Rows[r])
                                    {
                                        table.Cell()
                                            .Background(bgColor)
                                            .Padding(4)
                                            .Text(cell ?? "").FontSize(8);
                                    }
                                }
                            }
                        });

                        // Footer
                        page.Footer().AlignRight()
                            .Text(x =>
                            {
                                x.Span("Page ").FontSize(7).FontColor("#969696");
                                x.CurrentPageNumber().FontSize(7).FontColor("#969696");
                            });
                    });
                }
            });

            return document.GeneratePdf();
        }

        /// <summary>
        /// Convert report key + data into a list of PdfExportSection objects
        /// for the server-side PDF builder.
        /// </summary>
        private static List<PdfExportSection> BuildPdfSections(string reportKey, object data)
        {
            var sections = new List<PdfExportSection>();

            var keys = reportKey == "All"
                ? new[] { "PatientSummary", "DocumentActivity", "AuditLogs" }
                : new[] { reportKey };

            foreach (var key in keys)
            {
                switch (key)
                {
                    case "PatientSummary":
                        var ps = reportKey == "All"
                            ? ((AllReportsExportData)data).PatientSummary!
                            : (PatientSummaryReport)data;
                        sections.AddRange(BuildPatientSummaryPdfSections(ps));
                        break;
                    case "DocumentActivity":
                        var da = reportKey == "All"
                            ? ((AllReportsExportData)data).DocumentActivity!
                            : (DocumentActivityReport)data;
                        sections.AddRange(BuildDocumentActivityPdfSections(da));
                        break;
                    case "AuditLogs":
                        var al = reportKey == "All"
                            ? ((AllReportsExportData)data).AuditLogs!
                            : (AuditLogsReportData)data;
                        sections.AddRange(BuildAuditLogsPdfSections(al));
                        break;
                }
            }

            if (sections.Count == 0)
            {
                sections.Add(new PdfExportSection
                {
                    ReportTitle = "Report",
                    Title = "No Data",
                    Headers = new List<string> { "Info" },
                    Rows = new List<List<string>> { new() { "No data available for this report." } }
                });
            }

            return sections;
        }

        // Re-use the same section builders that the controllers used for JSON.
        // They are now static methods here so both controllers and PDF gen share one source.
        internal static List<PdfExportSection> BuildPatientSummaryPdfSections(PatientSummaryReport ps)
        {
            return new List<PdfExportSection>
            {
                new()
                {
                    ReportTitle = "Patient Summary Report",
                    Title = "Patient Overview",
                    Headers = new List<string> { "Metric", "Value" },
                    Rows = new List<List<string>>
                    {
                        new() { "Total Registered Patients", ps.TotalRegisteredPatients.ToString() },
                        new() { "New Patients \u2014 Today", ps.NewPatientsDaily.ToString() },
                        new() { "New Patients \u2014 Last 7 Days", ps.NewPatientsWeekly.ToString() },
                        new() { "New Patients \u2014 Last 30 Days", ps.NewPatientsMonthly.ToString() }
                    }
                },
                new()
                {
                    ReportTitle = "Patient Summary Report",
                    Title = "Age Distribution",
                    Headers = new List<string> { "Age Group", "Count", "Percentage" },
                    Rows = (ps.AgeDistribution ?? new())
                        .Select(r => new List<string> { r.AgeGroup, r.Count.ToString(), r.Percentage })
                        .ToList()
                },
                new()
                {
                    ReportTitle = "Patient Summary Report",
                    Title = "Gender Distribution",
                    Headers = new List<string> { "Gender", "Count", "Percentage" },
                    Rows = (ps.GenderDistribution ?? new())
                        .Select(r => new List<string> { r.Gender, r.Count.ToString(), r.Percentage })
                        .ToList()
                },
                new()
                {
                    ReportTitle = "Patient Summary Report",
                    Title = "Recently Active Patients",
                    Headers = new List<string> { "Patient ID", "Patient Name", "Gender", "Last Visit Date", "Total Documents" },
                    Rows = (ps.RecentlyActivePatients ?? new())
                        .Select(r => new List<string> { r.PatientID.ToString(), r.PatientName, r.Gender, r.LastVisitDate, r.TotalDocuments.ToString() })
                        .ToList()
                }
            };
        }

        internal static List<PdfExportSection> BuildDocumentActivityPdfSections(DocumentActivityReport da)
        {
            return new List<PdfExportSection>
            {
                new()
                {
                    ReportTitle = "Document Activity Report",
                    Title = "Document Overview",
                    Headers = new List<string> { "Metric", "Value" },
                    Rows = new List<List<string>>
                    {
                        new() { "Total Documents Uploaded", da.TotalDocumentsUploaded.ToString() },
                        new() { "Documents Uploaded \u2014 Today", da.DocumentsUploadedDaily.ToString() },
                        new() { "Documents Uploaded \u2014 Last 7 Days", da.DocumentsUploadedWeekly.ToString() },
                        new() { "Documents Uploaded \u2014 Last 30 Days", da.DocumentsUploadedMonthly.ToString() }
                    }
                },
                new()
                {
                    ReportTitle = "Document Activity Report",
                    Title = "Documents per Patient",
                    Headers = new List<string> { "Patient ID", "Patient Name", "Total Documents", "Document Types" },
                    Rows = (da.DocumentsPerPatient ?? new())
                        .Select(r => new List<string> { r.PatientID.ToString(), r.PatientName, r.TotalDocuments.ToString(), r.DocumentTypes })
                        .ToList()
                },
                new()
                {
                    ReportTitle = "Document Activity Report",
                    Title = "Documents per Category / Type",
                    Headers = new List<string> { "Document Type", "Count", "Percentage" },
                    Rows = (da.DocumentsPerCategory ?? new())
                        .Select(r => new List<string> { r.DocumentType, r.DocumentCount.ToString(), r.Percentage })
                        .ToList()
                },
                new()
                {
                    ReportTitle = "Document Activity Report",
                    Title = "Most Frequently Updated Documents",
                    Headers = new List<string> { "ID", "Document Title", "Type", "Patient", "Versions", "Last Updated" },
                    Rows = (da.FrequentlyUpdatedDocuments ?? new())
                        .Select(r => new List<string> { r.DocumentID.ToString(), r.DocumentTitle, r.DocumentType, r.PatientName, r.VersionCount.ToString(), r.LastUpdated })
                        .ToList()
                },
                new()
                {
                    ReportTitle = "Document Activity Report",
                    Title = "Version Count per Document",
                    Headers = new List<string> { "ID", "Document Title", "Type", "Patient", "Versions" },
                    Rows = (da.VersionCounts ?? new())
                        .Select(r => new List<string> { r.DocumentID.ToString(), r.DocumentTitle, r.DocumentType, r.PatientName, r.VersionCount.ToString() })
                        .ToList()
                }
            };
        }

        internal static List<PdfExportSection> BuildAuditLogsPdfSections(AuditLogsReportData al)
        {
            return new List<PdfExportSection>
            {
                new()
                {
                    ReportTitle = "Audit Logs Report",
                    Title = "Audit Log Entries",
                    Headers = new List<string> { "Log ID", "User", "Role", "Action", "Document", "Timestamp" },
                    Rows = (al.Rows ?? new())
                        .Select(r => new List<string> { r.LogID.ToString(), r.UserFullName, r.Role, r.Action, r.DocumentTitle, r.Timestamp })
                        .ToList()
                }
            };
        }

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
