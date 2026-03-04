using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DMS_CPMS.Data;
using DMS_CPMS.Data.Models;
using DMS_CPMS.Models.Reports;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DMS_CPMS.Services
{
    public interface IReportService
    {
        // ── Four structured report categories ──
        Task<PatientSummaryReport> GetPatientSummaryAsync(DateTime? from, DateTime? to);
        Task<DocumentActivityReport> GetDocumentActivityAsync(DateTime? from, DateTime? to, int page);
        Task<UserActivityReportData> GetUserActivityAsync(DateTime? from, DateTime? to, int page);
        Task<AuditLogsReportData> GetAuditLogsAsync(DateTime? from, DateTime? to, int page);

        // ── Export helpers (all rows, no pagination) ──
        Task<PatientSummaryReport> GetAllPatientSummaryDataAsync(DateTime? from, DateTime? to);
        Task<DocumentActivityReport> GetAllDocumentActivityDataAsync(DateTime? from, DateTime? to);
        Task<UserActivityReportData> GetAllUserActivityDataAsync(DateTime? from, DateTime? to);
        Task<AuditLogsReportData> GetAllAuditLogsDataAsync(DateTime? from, DateTime? to);
    }

    public class ReportService : IReportService
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private const int PageSize = 8;

        public ReportService(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // ═══════════════════════════════════════════════════════════
        //  1. PATIENT SUMMARY REPORT
        // ═══════════════════════════════════════════════════════════

        public async Task<PatientSummaryReport> GetPatientSummaryAsync(DateTime? from, DateTime? to)
        {
            return await BuildPatientSummaryReport(from, to);
        }

        public async Task<PatientSummaryReport> GetAllPatientSummaryDataAsync(DateTime? from, DateTime? to)
        {
            return await BuildPatientSummaryReport(from, to, allRows: true);
        }

        private async Task<PatientSummaryReport> BuildPatientSummaryReport(DateTime? from, DateTime? to, bool allRows = false)
        {
            var today = DateTime.Today;
            var allPatients = await _context.Patients
                .Include(p => p.Documents)
                .ToListAsync();

            var totalRegistered = allPatients.Count;
            var newDaily = allPatients.Count(p => p.VisitedAt.Date == today);
            var newWeekly = allPatients.Count(p => p.VisitedAt.Date >= today.AddDays(-6));
            var newMonthly = allPatients.Count(p => p.VisitedAt.Date >= today.AddMonths(-1).AddDays(1));

            // Age Distribution
            var ageGroups = allPatients
                .Select(p =>
                {
                    var age = today.Year - p.BirthDate.Year;
                    if (p.BirthDate.Date > today.AddYears(-age)) age--;
                    return age;
                })
                .GroupBy(age => age switch
                {
                    < 1 => "Infant (< 1)",
                    < 13 => "Child (1-12)",
                    < 18 => "Adolescent (13-17)",
                    < 30 => "Young Adult (18-29)",
                    < 45 => "Adult (30-44)",
                    < 60 => "Middle-Aged (45-59)",
                    _ => "Senior (60+)"
                })
                .Select(g => new AgeDistributionRow
                {
                    AgeGroup = g.Key,
                    Count = g.Count(),
                    Percentage = totalRegistered > 0
                        ? $"{(g.Count() * 100.0 / totalRegistered):F1}%"
                        : "0.0%"
                })
                .OrderBy(r => r.AgeGroup switch
                {
                    "Infant (< 1)" => 0,
                    "Child (1-12)" => 1,
                    "Adolescent (13-17)" => 2,
                    "Young Adult (18-29)" => 3,
                    "Adult (30-44)" => 4,
                    "Middle-Aged (45-59)" => 5,
                    _ => 6
                })
                .ToList();

            // Gender Distribution
            var genderGroups = allPatients
                .GroupBy(p => string.IsNullOrWhiteSpace(p.Gender) ? "Not Specified" : p.Gender)
                .Select(g => new GenderDistributionRow
                {
                    Gender = g.Key,
                    Count = g.Count(),
                    Percentage = totalRegistered > 0
                        ? $"{(g.Count() * 100.0 / totalRegistered):F1}%"
                        : "0.0%"
                })
                .OrderByDescending(r => r.Count)
                .ToList();

            // Recently Active Patients
            var recentPatientsQuery = allPatients.AsEnumerable();
            if (from.HasValue) recentPatientsQuery = recentPatientsQuery.Where(p => p.VisitedAt >= from.Value);
            if (to.HasValue) recentPatientsQuery = recentPatientsQuery.Where(p => p.VisitedAt < to.Value.AddDays(1));

            var recentlyActive = recentPatientsQuery
                .OrderByDescending(p => p.VisitedAt);

            var recentlyActiveList = (allRows ? recentlyActive : recentlyActive.Take(10))
                .Select(p => new RecentlyActivePatientRow
                {
                    PatientID = p.PatientID,
                    PatientName = p.FirstName + " " + p.LastName,
                    Gender = p.Gender,
                    LastVisitDate = p.VisitedAt.ToString("MMM dd, yyyy"),
                    TotalDocuments = p.Documents.Count(d => !d.IsArchived)
                })
                .ToList();

            return new PatientSummaryReport
            {
                TotalRegisteredPatients = totalRegistered,
                NewPatientsDaily = newDaily,
                NewPatientsWeekly = newWeekly,
                NewPatientsMonthly = newMonthly,
                AgeDistribution = ageGroups,
                GenderDistribution = genderGroups,
                RecentlyActivePatients = recentlyActiveList
            };
        }

        // ═══════════════════════════════════════════════════════════
        //  2. DOCUMENT ACTIVITY REPORT
        // ═══════════════════════════════════════════════════════════

        public async Task<DocumentActivityReport> GetDocumentActivityAsync(DateTime? from, DateTime? to, int page)
        {
            return await BuildDocumentActivityReport(from, to, page);
        }

        public async Task<DocumentActivityReport> GetAllDocumentActivityDataAsync(DateTime? from, DateTime? to)
        {
            return await BuildDocumentActivityReport(from, to, 1, allRows: true);
        }

        private async Task<DocumentActivityReport> BuildDocumentActivityReport(DateTime? from, DateTime? to, int page, bool allRows = false)
        {
            var today = DateTime.Today;

            var allDocuments = await _context.Documents
                .Where(d => !d.IsArchived)
                .Include(d => d.Patient)
                .Include(d => d.Versions)
                .ToListAsync();

            var totalDocsUploaded = allDocuments.Count;
            var docsDaily = allDocuments.Count(d => d.UploadDate.Date == today);
            var docsWeekly = allDocuments.Count(d => d.UploadDate.Date >= today.AddDays(-6));
            var docsMonthly = allDocuments.Count(d => d.UploadDate.Date >= today.AddMonths(-1).AddDays(1));

            // Documents per Patient
            // Filter by document UploadDate (not patient VisitedAt) so the date range
            // correctly scopes which documents are counted.  For export (allRows=true)
            // we always include every patient so no records are silently omitted.
            var allPatients = await _context.Patients
                .Include(p => p.Documents)
                .ToListAsync();

            var docsPerPatientAll = allPatients
                .Select(p =>
                {
                    // Apply date filter to individual documents, not to the patient row
                    var patientDocs = p.Documents
                        .Where(d => !d.IsArchived)
                        .Where(d => !from.HasValue || d.UploadDate >= from.Value)
                        .Where(d => !to.HasValue   || d.UploadDate < to.Value.AddDays(1))
                        .ToList();

                    // For export (allRows) include every patient; for the paginated view
                    // only include patients that have at least one document in range.
                    return new
                    {
                        Patient = p,
                        Docs = patientDocs
                    };
                })
                .Where(x => allRows || x.Docs.Count > 0)
                .OrderByDescending(x => x.Docs.Count)
                .ThenBy(x => x.Patient.LastName)
                .Select(x => new DocumentsPerPatientRow
                {
                    PatientID = x.Patient.PatientID,
                    PatientName = x.Patient.FirstName + " " + x.Patient.LastName,
                    TotalDocuments = x.Docs.Count,
                    DocumentTypes = x.Docs.Select(d => d.DocumentType).Distinct().Any()
                        ? string.Join(", ", x.Docs.Select(d => d.DocumentType).Distinct())
                        : "None"
                })
                .ToList();

            var totalPatientCount = docsPerPatientAll.Count;
            var totalPages = (int)Math.Ceiling(totalPatientCount / (double)PageSize);
            page = Math.Max(1, Math.Min(page, Math.Max(1, totalPages)));
            var docsPerPatientPage = allRows
                ? docsPerPatientAll
                : docsPerPatientAll.Skip((page - 1) * PageSize).Take(PageSize).ToList();

            // Build a date-filtered document list for detail tables so that when a
            // date range is chosen the category/version tables reflect the same window.
            var filteredDocuments = allDocuments
                .Where(d => !from.HasValue || d.UploadDate >= from.Value)
                .Where(d => !to.HasValue   || d.UploadDate < to.Value.AddDays(1))
                .ToList();

            // Documents per Category/Type — filtered by upload date
            int filteredTotal = filteredDocuments.Count;
            var docsPerCategory = filteredDocuments
                .GroupBy(d => string.IsNullOrWhiteSpace(d.DocumentType) ? "Uncategorized" : d.DocumentType)
                .Select(g => new DocumentsPerCategoryRow
                {
                    DocumentType = g.Key,
                    DocumentCount = g.Count(),
                    Percentage = filteredTotal > 0
                        ? $"{(g.Count() * 100.0 / filteredTotal):F1}%"
                        : "0.0%"
                })
                .OrderByDescending(r => r.DocumentCount)
                .ToList();

            // Most Frequently Updated Documents — filtered by upload date
            var frequentlyUpdatedQuery = filteredDocuments
                .Where(d => d.Versions.Count > 0)
                .OrderByDescending(d => d.Versions.Count);
            var frequentlyUpdated = (allRows ? frequentlyUpdatedQuery : frequentlyUpdatedQuery.Take(10))
                .Select(d => new FrequentlyUpdatedDocumentRow
                {
                    DocumentID = d.DocumentID,
                    DocumentTitle = d.DocumentTitle,
                    DocumentType = d.DocumentType,
                    PatientName = d.Patient != null
                        ? d.Patient.FirstName + " " + d.Patient.LastName
                        : "—",
                    VersionCount = d.Versions.Count,
                    LastUpdated = d.Versions.Any()
                        ? d.Versions.Max(v => v.CreatedDate).ToString("MMM dd, yyyy")
                        : d.UploadDate.ToString("MMM dd, yyyy")
                })
                .ToList();

            // Version Count per Document — filtered by upload date
            var versionCountQuery = filteredDocuments
                .OrderByDescending(d => d.Versions.Count);
            var versionCounts = (allRows ? versionCountQuery : versionCountQuery.Take(10))
                .Select(d => new VersionCountRow
                {
                    DocumentID = d.DocumentID,
                    DocumentTitle = d.DocumentTitle,
                    DocumentType = d.DocumentType,
                    PatientName = d.Patient != null
                        ? d.Patient.FirstName + " " + d.Patient.LastName
                        : "—",
                    VersionCount = d.Versions.Count
                })
                .ToList();

            return new DocumentActivityReport
            {
                TotalDocumentsUploaded = totalDocsUploaded,
                DocumentsUploadedDaily = docsDaily,
                DocumentsUploadedWeekly = docsWeekly,
                DocumentsUploadedMonthly = docsMonthly,
                DocumentsPerPatient = docsPerPatientPage,
                DocumentsPerCategory = docsPerCategory,
                FrequentlyUpdatedDocuments = frequentlyUpdated,
                VersionCounts = versionCounts,
                PageNumber = page,
                PageSize = PageSize,
                TotalPages = totalPages,
                TotalCount = totalPatientCount
            };
        }

        // ═══════════════════════════════════════════════════════════
        //  3. USER / ACCOUNT ACTIVITY REPORT
        // ═══════════════════════════════════════════════════════════

        public async Task<UserActivityReportData> GetUserActivityAsync(DateTime? from, DateTime? to, int page)
        {
            return await BuildUserActivityReport(from, to, page);
        }

        public async Task<UserActivityReportData> GetAllUserActivityDataAsync(DateTime? from, DateTime? to)
        {
            return await BuildUserActivityReport(from, to, 1, allRows: true);
        }

        private async Task<UserActivityReportData> BuildUserActivityReport(DateTime? from, DateTime? to, int page, bool allRows = false)
        {
            var uploadActions = new[] { "Upload Document", "Upload Document (Google Drive)" };
            var editActions = new[] { "Edit Document", "Edit Document Title", "Edit Document Type" };
            var deleteActions = new[] { "Delete Document", "Permanent Delete", "Permanent Delete Version" };

            // ── Active User Counts ──
            var adminUsers = await _userManager.GetUsersInRoleAsync("Admin");
            var staffUsers = await _userManager.GetUsersInRoleAsync("Staff");
            var allUsers = await _context.Users.ToListAsync();
            var totalActive = allUsers.Count(u => u.IsActive);
            var totalInactive = allUsers.Count(u => !u.IsActive);

            // ── Login Activity Logs ──
            var loginActions = new[] { "Login", "Failed Login", "Login Attempt (Deactivated)" };
            var loginQuery = _context.AuditLogs
                .Include(a => a.User)
                .Where(a => loginActions.Contains(a.Action) && a.UserID != null);

            if (from.HasValue) loginQuery = loginQuery.Where(a => a.Timestamp >= from.Value);
            if (to.HasValue) loginQuery = loginQuery.Where(a => a.Timestamp < to.Value.AddDays(1));

            var loginTotalCount = await loginQuery.CountAsync();
            var loginTotalPages = (int)Math.Ceiling(loginTotalCount / (double)PageSize);
            page = Math.Max(1, Math.Min(page, Math.Max(1, loginTotalPages)));

            var loginLogsQuery = loginQuery.OrderByDescending(a => a.Timestamp);
            var loginLogs = allRows
                ? await loginLogsQuery.Select(a => new LoginActivityRow
                {
                    FullName = a.User != null ? (a.User.FirstName + " " + a.User.LastName) : "Unknown",
                    Role = a.Role ?? "Unknown",
                    Action = a.Action,
                    Timestamp = a.Timestamp.ToString("MMM dd, yyyy hh:mm tt")
                }).ToListAsync()
                : await loginLogsQuery
                    .Skip((page - 1) * PageSize).Take(PageSize)
                    .Select(a => new LoginActivityRow
                    {
                        FullName = a.User != null ? (a.User.FirstName + " " + a.User.LastName) : "Unknown",
                        Role = a.Role ?? "Unknown",
                        Action = a.Action,
                        Timestamp = a.Timestamp.ToString("MMM dd, yyyy hh:mm tt")
                    }).ToListAsync();

            // ── Last Login per User ──
            var lastLoginPerUser = await _context.AuditLogs
                .Include(a => a.User)
                .Where(a => a.Action == "Login" && a.UserID != null)
                .GroupBy(a => new { a.UserID, a.User!.FirstName, a.User.LastName, a.User.IsActive })
                .Select(g => new
                {
                    FullName = g.Key.FirstName + " " + g.Key.LastName,
                    IsActive = g.Key.IsActive,
                    LastLogin = g.Max(a => a.Timestamp),
                    LatestRole = g.OrderByDescending(a => a.Timestamp).Select(a => a.Role).FirstOrDefault()
                })
                .OrderByDescending(g => g.LastLogin)
                .ToListAsync();

            var lastLoginRows = lastLoginPerUser.Select(g => new LastLoginRow
            {
                FullName = g.FullName,
                Role = g.LatestRole ?? "Unknown",
                AccountStatus = g.IsActive ? "Active" : "Inactive",
                LastLogin = g.LastLogin.ToString("MMM dd, yyyy hh:mm tt")
            }).ToList();

            // ── Actions Performed (grouped by user + action type) ──
            var actionsQuery = _context.AuditLogs
                .Include(a => a.User)
                .Where(a => a.UserID != null);

            if (from.HasValue) actionsQuery = actionsQuery.Where(a => a.Timestamp >= from.Value);
            if (to.HasValue) actionsQuery = actionsQuery.Where(a => a.Timestamp < to.Value.AddDays(1));

            var actionsGrouped = await actionsQuery
                .GroupBy(a => new { a.UserID, a.User!.FirstName, a.User.LastName, a.Action })
                .Select(g => new
                {
                    FullName = g.Key.FirstName + " " + g.Key.LastName,
                    Action = g.Key.Action,
                    Count = g.Count(),
                    LastPerformed = g.Max(a => a.Timestamp),
                    LatestRole = g.OrderByDescending(a => a.Timestamp).Select(a => a.Role).FirstOrDefault()
                })
                .OrderByDescending(g => g.Count)
                .Take(50)
                .ToListAsync();

            var actionRows = actionsGrouped.Select(g => new UserActionRow
            {
                FullName = g.FullName,
                Role = g.LatestRole ?? "Unknown",
                Action = g.Action,
                Count = g.Count,
                LastPerformed = g.LastPerformed.ToString("MMM dd, yyyy hh:mm tt")
            }).ToList();

            // ── Most Active Staff Members ──
            var staffQuery = _context.AuditLogs
                .Include(a => a.User)
                .Where(a => a.UserID != null && (a.Role == "Staff" || a.Role == "Admin"));

            if (from.HasValue) staffQuery = staffQuery.Where(a => a.Timestamp >= from.Value);
            if (to.HasValue) staffQuery = staffQuery.Where(a => a.Timestamp < to.Value.AddDays(1));

            var staffGrouped = await staffQuery
                .GroupBy(a => new { a.UserID, a.User!.FirstName, a.User.LastName })
                .Select(g => new
                {
                    FullName = g.Key.FirstName + " " + g.Key.LastName,
                    TotalActions = g.Count(),
                    Uploads = g.Count(a => uploadActions.Contains(a.Action)),
                    Edits = g.Count(a => editActions.Contains(a.Action)),
                    Deletes = g.Count(a => deleteActions.Contains(a.Action)),
                    LastActivity = g.Max(a => a.Timestamp),
                    LatestRole = g.OrderByDescending(a => a.Timestamp).Select(a => a.Role).FirstOrDefault()
                })
                .OrderByDescending(g => g.TotalActions)
                .Take(10)
                .ToListAsync();

            var mostActiveStaff = staffGrouped.Select((g, i) => new MostActiveStaffRow
            {
                Rank = i + 1,
                FullName = g.FullName,
                Role = g.LatestRole ?? "Unknown",
                TotalActions = g.TotalActions,
                Uploads = g.Uploads,
                Edits = g.Edits,
                Deletes = g.Deletes,
                LastActivity = g.LastActivity.ToString("MMM dd, yyyy hh:mm tt")
            }).ToList();

            return new UserActivityReportData
            {
                TotalAdminUsers = adminUsers.Count(u => u.IsActive),
                TotalStaffUsers = staffUsers.Count(u => u.IsActive),
                TotalActiveUsers = totalActive,
                TotalInactiveUsers = totalInactive,
                LoginActivity = loginLogs,
                LastLoginPerUser = lastLoginRows,
                ActionsPerformed = actionRows,
                MostActiveStaff = mostActiveStaff,
                PageNumber = page,
                PageSize = PageSize,
                TotalPages = loginTotalPages,
                TotalCount = loginTotalCount
            };
        }

        // ═══════════════════════════════════════════════════════════
        //  4. AUDIT LOGS REPORT
        // ═══════════════════════════════════════════════════════════

        public async Task<AuditLogsReportData> GetAuditLogsAsync(DateTime? from, DateTime? to, int page)
        {
            return await BuildAuditLogsReport(from, to, page);
        }

        public async Task<AuditLogsReportData> GetAllAuditLogsDataAsync(DateTime? from, DateTime? to)
        {
            return await BuildAuditLogsReport(from, to, 1, allRows: true);
        }

        private async Task<AuditLogsReportData> BuildAuditLogsReport(DateTime? from, DateTime? to, int page, bool allRows = false)
        {
            var query = _context.AuditLogs
                .Include(a => a.User)
                .Include(a => a.Document)
                .AsQueryable();

            if (from.HasValue) query = query.Where(a => a.Timestamp >= from.Value);
            if (to.HasValue) query = query.Where(a => a.Timestamp < to.Value.AddDays(1));

            var totalCount = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalCount / (double)PageSize);
            page = Math.Max(1, Math.Min(page, Math.Max(1, totalPages)));

            var orderedQuery = query.OrderByDescending(a => a.Timestamp);

            var logs = allRows
                ? await orderedQuery.Select(a => new AuditLogReportRow
                {
                    LogID = a.LogID,
                    UserFullName = a.User != null ? (a.User.FirstName + " " + a.User.LastName) : "Unknown",
                    Role = a.Role ?? "Unknown",
                    Action = a.Action,
                    DocumentTitle = a.Document != null ? a.Document.DocumentTitle : "—",
                    Timestamp = a.Timestamp.ToString("MMM dd, yyyy hh:mm tt")
                }).ToListAsync()
                : await orderedQuery
                    .Skip((page - 1) * PageSize).Take(PageSize)
                    .Select(a => new AuditLogReportRow
                    {
                        LogID = a.LogID,
                        UserFullName = a.User != null ? (a.User.FirstName + " " + a.User.LastName) : "Unknown",
                        Role = a.Role ?? "Unknown",
                        Action = a.Action,
                        DocumentTitle = a.Document != null ? a.Document.DocumentTitle : "—",
                        Timestamp = a.Timestamp.ToString("MMM dd, yyyy hh:mm tt")
                    }).ToListAsync();

            return new AuditLogsReportData
            {
                Rows = logs,
                TotalEntries = totalCount,
                PageNumber = page,
                PageSize = PageSize,
                TotalPages = totalPages,
                TotalCount = totalCount
            };
        }
    }
}
