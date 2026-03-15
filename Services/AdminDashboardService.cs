using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DMS_CPMS.Data;
using DMS_CPMS.Data.Models;
using DMS_CPMS.Models.Admin;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DMS_CPMS.Services
{
    public interface IAdminDashboardService
    {
        Task<AdminDashboardViewModel> GetDashboardAsync(DateTime? from, DateTime? to);
        Task<AdminDashboardDataResponse> GetDashboardDataAsync(string? range, string? startDate, string? endDate);
    }

    public class AdminDashboardService : IAdminDashboardService
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public AdminDashboardService(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // ─────────────────── Full dashboard (initial load) ───────────────
        public async Task<AdminDashboardViewModel> GetDashboardAsync(DateTime? from, DateTime? to)
        {
            var now = DateTime.Now;
            var today = DateTime.Today;

            var model = new AdminDashboardViewModel
            {
                // KPIs
                TotalPatients = await CountPatients(from, to),
                NewPatients = await CountNewPatients(from, to, today),
                TotalDocuments = await CountDocuments(from, to),
                TotalArchiveDocuments = await CountArchiveDocuments(from, to),
                ActiveStaff = await CountActiveStaff(),

                // Patient charts
                PatientsByGender = await GetPatientsByGender(from, to),
                PatientsByAgeGroup = await GetPatientsByAgeGroup(from, to),
                NewPatientsPerMonth = await GetNewPatientsPerMonth(from, to, now),

                // Document analytics
                DocumentUploadTrend = await GetDocumentUploadTrend(null, from, to, now),
                DocumentTrendTitle = "Documents Uploaded Per Month",
                DocumentsByType = await GetDocumentsByType(from, to),
                MostUpdatedDocuments = await GetMostUpdatedDocuments(from, to),
                TotalDocumentVersions = await CountDocumentVersions(from, to),

                // Recent activity
                RecentActivities = await GetRecentActivities(null, null, 15)
            };

            return model;
        }

        // ─────────────────── AJAX date-filter endpoint ───────────────────
        public async Task<AdminDashboardDataResponse> GetDashboardDataAsync(string? range, string? startDate, string? endDate)
        {
            DateTime? from = null;
            DateTime? to = null;
            var now = DateTime.Now;
            var today = DateTime.Today;

            switch (range)
            {
                case "today":
                    from = today;
                    to = now;
                    break;
                case "weekly":
                    from = today.AddDays(-7);
                    to = now;
                    break;
                case "monthly":
                    from = today.AddDays(-30);
                    to = now;
                    break;
                case "yearly":
                    from = today.AddYears(-1);
                    to = now;
                    break;
                case "custom":
                    if (DateTime.TryParse(startDate, out var s) && DateTime.TryParse(endDate, out var e))
                    {
                        from = s.Date;
                        to = e.Date.AddDays(1).AddSeconds(-1);
                    }
                    break;
                // "all" or null → no filter
            }

            // Dynamic trend title
            var trendTitle = DetermineTrendTitle(range, from, to);

            return new AdminDashboardDataResponse
            {
                TotalPatients = await CountPatients(from, to),
                NewPatients = await CountNewPatients(from, to, today),
                TotalDocuments = await CountDocuments(from, to),
                TotalArchiveDocuments = await CountArchiveDocuments(from, to),
                ActiveStaff = await CountActiveStaff(),

                PatientsByGender = await GetPatientsByGender(from, to),
                PatientsByAgeGroup = await GetPatientsByAgeGroup(from, to),
                NewPatientsPerMonth = await GetNewPatientsPerMonth(from, to, now),

                DocumentUploadTrend = await GetDocumentUploadTrend(range, from, to, now),
                DocumentTrendTitle = trendTitle,
                DocumentsByType = await GetDocumentsByType(from, to),
                MostUpdatedDocuments = await GetMostUpdatedDocuments(from, to),
                TotalDocumentVersions = await CountDocumentVersions(from, to),

                RecentActivities = await GetRecentActivities(from, to, 15)
            };
        }

        // ═══════════ PRIVATE HELPERS ═══════════

        // ── KPI Counting ──

        private async Task<int> CountPatients(DateTime? from, DateTime? to)
        {
            var q = _context.Patients.AsQueryable();
            if (from.HasValue && to.HasValue)
                q = q.Where(p => p.VisitedAt >= from.Value && p.VisitedAt <= to.Value);
            return await q.CountAsync();
        }

        private async Task<int> CountNewPatients(DateTime? from, DateTime? to, DateTime today)
        {
            var q = _context.Patients.AsQueryable();
            if (from.HasValue && to.HasValue)
                q = q.Where(p => p.VisitedAt >= from.Value && p.VisitedAt <= to.Value);
            else
                q = q.Where(p => p.VisitedAt >= today.AddDays(-30)); // default: last 30 days
            return await q.CountAsync();
        }

        private async Task<int> CountDocuments(DateTime? from, DateTime? to)
        {
            var q = _context.Documents.AsQueryable();
            if (from.HasValue && to.HasValue)
                q = q.Where(d => d.UploadDate >= from.Value && d.UploadDate <= to.Value);
            return await q.CountAsync();
        }

        private async Task<int> CountDocumentsToday(DateTime today)
        {
            return await _context.Documents.CountAsync(d => d.UploadDate >= today);
        }

        private async Task<int> CountArchiveDocuments(DateTime? from, DateTime? to)
        {
            var q = _context.ArchiveDocuments.AsQueryable();
            if (from.HasValue && to.HasValue)
                q = q.Where(a => a.ArchiveDate >= from.Value && a.ArchiveDate <= to.Value);
            return await q.CountAsync();
        }

        private async Task<int> CountActiveStaff()
        {
            var staffUsers = await _userManager.GetUsersInRoleAsync("Staff");
            return staffUsers.Count(u => u.IsActive && !u.IsArchived);
        }

        private async Task<int> CountDocumentVersions(DateTime? from, DateTime? to)
        {
            var q = _context.DocumentVersions.AsQueryable();
            if (from.HasValue && to.HasValue)
                q = q.Where(v => v.CreatedDate >= from.Value && v.CreatedDate <= to.Value);
            return await q.CountAsync();
        }

        // ── Patient Charts ──

        private async Task<List<ChartDataPoint>> GetPatientsByGender(DateTime? from, DateTime? to)
        {
            var q = _context.Patients.AsQueryable();
            if (from.HasValue && to.HasValue)
                q = q.Where(p => p.VisitedAt >= from.Value && p.VisitedAt <= to.Value);

            return await q
                .GroupBy(p => p.Gender)
                .Select(g => new ChartDataPoint { Label = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ToListAsync();
        }

        private async Task<List<ChartDataPoint>> GetPatientsByAgeGroup(DateTime? from, DateTime? to)
        {
            var q = _context.Patients.AsQueryable();
            if (from.HasValue && to.HasValue)
                q = q.Where(p => p.VisitedAt >= from.Value && p.VisitedAt <= to.Value);

            var patients = await q.Select(p => p.BirthDate).ToListAsync();
            var now = DateTime.Now;

            var groups = new Dictionary<string, int>
            {
                ["0-17"] = 0,
                ["18-30"] = 0,
                ["31-45"] = 0,
                ["46-60"] = 0,
                ["61+"] = 0
            };

            foreach (var bd in patients)
            {
                var age = now.Year - bd.Year;
                if (bd.Date > now.AddYears(-age)) age--;

                if (age <= 17) groups["0-17"]++;
                else if (age <= 30) groups["18-30"]++;
                else if (age <= 45) groups["31-45"]++;
                else if (age <= 60) groups["46-60"]++;
                else groups["61+"]++;
            }

            return groups.Select(g => new ChartDataPoint { Label = g.Key, Count = g.Value }).ToList();
        }

        private async Task<List<ChartDataPoint>> GetNewPatientsPerMonth(DateTime? from, DateTime? to, DateTime now)
        {
            DateTime rangeStart, rangeEnd;
            if (from.HasValue && to.HasValue)
            {
                rangeStart = new DateTime(from.Value.Year, from.Value.Month, 1);
                rangeEnd = to.Value;
            }
            else
            {
                rangeStart = new DateTime(now.Year, now.Month, 1).AddMonths(-5);
                rangeEnd = now;
            }

            var data = await _context.Patients
                .Where(p => p.VisitedAt >= rangeStart && p.VisitedAt <= rangeEnd)
                .GroupBy(p => new { p.VisitedAt.Year, p.VisitedAt.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
                .ToListAsync();

            var result = new List<ChartDataPoint>();
            var current = rangeStart;
            var endMonth = new DateTime(rangeEnd.Year, rangeEnd.Month, 1);
            while (current <= endMonth)
            {
                var match = data.FirstOrDefault(d => d.Year == current.Year && d.Month == current.Month);
                result.Add(new ChartDataPoint
                {
                    Label = current.ToString("MMM yyyy"),
                    Count = match?.Count ?? 0
                });
                current = current.AddMonths(1);
            }

            return result;
        }

        // ── Document Analytics ──

        private async Task<List<ChartDataPoint>> GetDocumentUploadTrend(string? range, DateTime? from, DateTime? to, DateTime now)
        {
            // Determine grouping: hourly / daily / monthly
            string mode;
            DateTime rangeStart, rangeEnd;

            switch (range)
            {
                case "today":
                    mode = "hourly";
                    rangeStart = DateTime.Today;
                    rangeEnd = now;
                    break;
                case "weekly":
                    mode = "daily";
                    rangeStart = DateTime.Today.AddDays(-6);
                    rangeEnd = now;
                    break;
                case "monthly":
                    mode = "daily";
                    rangeStart = DateTime.Today.AddDays(-29);
                    rangeEnd = now;
                    break;
                case "custom":
                    if (from.HasValue && to.HasValue)
                    {
                        rangeStart = from.Value;
                        rangeEnd = to.Value;
                        var span = (rangeEnd - rangeStart).TotalDays;
                        mode = span <= 1 ? "hourly" : span <= 14 ? "daily" : "monthly";
                    }
                    else
                    {
                        rangeStart = now.AddMonths(-5);
                        rangeEnd = now;
                        mode = "monthly";
                    }
                    break;
                default: // "yearly", "all", null
                    if (from.HasValue && to.HasValue)
                    {
                        rangeStart = from.Value;
                        rangeEnd = to.Value;
                    }
                    else
                    {
                        rangeStart = now.AddMonths(-5);
                        rangeEnd = now;
                    }
                    mode = "monthly";
                    break;
            }

            return mode switch
            {
                "hourly" => await GetHourlyDocTrend(rangeStart, rangeEnd),
                "daily" => await GetDailyDocTrend(rangeStart, rangeEnd),
                _ => await GetMonthlyDocTrend(rangeStart, rangeEnd)
            };
        }

        private async Task<List<ChartDataPoint>> GetHourlyDocTrend(DateTime from, DateTime to)
        {
            var uploads = await _context.Documents
                .Where(d => d.UploadDate >= from && d.UploadDate <= to)
                .GroupBy(d => d.UploadDate.Hour)
                .Select(g => new { Hour = g.Key, Count = g.Count() })
                .ToListAsync();

            var result = new List<ChartDataPoint>();
            for (int h = 0; h < 24; h++)
            {
                var match = uploads.FirstOrDefault(u => u.Hour == h);
                var ampm = h < 12 ? "AM" : "PM";
                var hour12 = h == 0 ? 12 : h > 12 ? h - 12 : h;
                result.Add(new ChartDataPoint { Label = $"{hour12} {ampm}", Count = match?.Count ?? 0 });
            }
            return result;
        }

        private async Task<List<ChartDataPoint>> GetDailyDocTrend(DateTime from, DateTime to)
        {
            var startDate = from.Date;
            var endDate = to.Date;

            var uploads = await _context.Documents
                .Where(d => d.UploadDate >= startDate && d.UploadDate <= to)
                .GroupBy(d => d.UploadDate.Date)
                .Select(g => new { Date = g.Key, Count = g.Count() })
                .ToListAsync();

            var result = new List<ChartDataPoint>();
            var current = startDate;
            while (current <= endDate)
            {
                var match = uploads.FirstOrDefault(u => u.Date == current);
                result.Add(new ChartDataPoint { Label = current.ToString("MMM dd"), Count = match?.Count ?? 0 });
                current = current.AddDays(1);
            }
            return result;
        }

        private async Task<List<ChartDataPoint>> GetMonthlyDocTrend(DateTime from, DateTime to)
        {
            var startMonth = new DateTime(from.Year, from.Month, 1);
            var endMonth = new DateTime(to.Year, to.Month, 1);

            var uploads = await _context.Documents
                .Where(d => d.UploadDate >= startMonth && d.UploadDate <= to)
                .GroupBy(d => new { d.UploadDate.Year, d.UploadDate.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
                .ToListAsync();

            var result = new List<ChartDataPoint>();
            var current = startMonth;
            while (current <= endMonth)
            {
                var match = uploads.FirstOrDefault(u => u.Year == current.Year && u.Month == current.Month);
                result.Add(new ChartDataPoint { Label = current.ToString("MMM yyyy"), Count = match?.Count ?? 0 });
                current = current.AddMonths(1);
            }
            return result;
        }

        private async Task<List<ChartDataPoint>> GetDocumentsByType(DateTime? from, DateTime? to)
        {
            var q = _context.Documents.AsQueryable();
            if (from.HasValue && to.HasValue)
                q = q.Where(d => d.UploadDate >= from.Value && d.UploadDate <= to.Value);

            return await q
                .GroupBy(d => d.DocumentType)
                .Select(g => new ChartDataPoint { Label = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ToListAsync();
        }

        private async Task<List<MostUpdatedDocItem>> GetMostUpdatedDocuments(DateTime? from, DateTime? to)
        {
            var q = _context.DocumentVersions
                .Include(v => v.Document)
                    .ThenInclude(d => d.Patient)
                .AsQueryable();

            if (from.HasValue && to.HasValue)
                q = q.Where(v => v.CreatedDate >= from.Value && v.CreatedDate <= to.Value);

            var grouped = await q
                .GroupBy(v => new { v.DocumentID, v.Document.DocumentTitle, PatientFirst = v.Document.Patient.FirstName, PatientLast = v.Document.Patient.LastName })
                .Select(g => new MostUpdatedDocItem
                {
                    DocumentID = g.Key.DocumentID,
                    DocumentTitle = g.Key.DocumentTitle,
                    PatientName = g.Key.PatientFirst + " " + g.Key.PatientLast,
                    VersionCount = g.Count(),
                    LastUpdated = g.Max(v => v.CreatedDate)
                })
                .OrderByDescending(x => x.VersionCount)
                .Take(5)
                .ToListAsync();

            return grouped;
        }

        // ── Recent Activity ──

        private async Task<List<DashboardActivityItem>> GetRecentActivities(DateTime? from, DateTime? to, int count)
        {
            var q = _context.AuditLogs
                .Include(a => a.User)
                .Include(a => a.Document)
                    .ThenInclude(d => d!.Patient)
                .AsQueryable();

            if (from.HasValue && to.HasValue)
                q = q.Where(a => a.Timestamp >= from.Value && a.Timestamp <= to.Value);

            var logs = await q
                .OrderByDescending(a => a.Timestamp)
                .Take(count)
                .ToListAsync();

            var now = DateTime.Now;
            var activities = new List<DashboardActivityItem>();

            foreach (var log in logs)
            {
                var userName = log.User != null
                    ? $"{log.User.FirstName} {log.User.LastName}".Trim()
                    : "System";

                var roleName = log.Role ?? "";

                // Build a human-readable description
                var description = BuildActivityDescription(log, userName, roleName);
                var actionType = CategorizeAction(log.Action);

                activities.Add(new DashboardActivityItem
                {
                    Description = description,
                    ActionType = actionType,
                    Timestamp = log.Timestamp,
                    TimeAgo = FormatTimeAgo(log.Timestamp, now)
                });
            }

            return activities;
        }

        private static string BuildActivityDescription(AuditLog log, string userName, string roleName)
        {
            var docTitle = log.Document?.DocumentTitle;
            var patientName = log.Document?.Patient != null
                ? $"{log.Document.Patient.FirstName} {log.Document.Patient.LastName}"
                : null;

            var roleLabel = !string.IsNullOrEmpty(roleName) ? roleName : "User";

            return log.Action switch
            {
                "Upload Document" => !string.IsNullOrEmpty(patientName)
                    ? $"{userName} uploaded {docTitle} for {patientName}"
                    : $"{userName} uploaded a document",

                "Edit Document" => !string.IsNullOrEmpty(docTitle)
                    ? $"{userName} updated {docTitle}"
                    : $"{userName} updated a document",

                "Archive Document" => !string.IsNullOrEmpty(docTitle)
                    ? $"{userName} archived {docTitle}"
                    : $"{userName} archived a document",

                "Restore Document" or "Restore Version" => !string.IsNullOrEmpty(docTitle)
                    ? $"{userName} restored {docTitle}"
                    : $"{userName} restored a document",

                "Delete Document" => !string.IsNullOrEmpty(docTitle)
                    ? $"{userName} deleted {docTitle}"
                    : $"{userName} deleted a document",

                "Download Document" => !string.IsNullOrEmpty(docTitle)
                    ? $"{userName} downloaded {docTitle}"
                    : $"{userName} downloaded a document",

                "Create Account" => $"Account created for {userName}",

                "Login" => $"{userName} logged in",

                _ => !string.IsNullOrEmpty(docTitle)
                    ? $"{userName} performed \"{log.Action}\" on {docTitle}"
                    : $"{userName} performed \"{log.Action}\""
            };
        }

        private static string CategorizeAction(string action)
        {
            return action switch
            {
                "Upload Document" => "upload",
                "Edit Document" => "update",
                "Archive Document" => "archive",
                "Restore Document" or "Restore Version" => "restore",
                "Delete Document" => "delete",
                "Download Document" => "download",
                "Create Account" => "account",
                "Login" => "login",
                _ => "default"
            };
        }

        private static string FormatTimeAgo(DateTime timestamp, DateTime now)
        {
            var diff = now - timestamp;

            if (diff.TotalMinutes < 1) return "Just now";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} min ago";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
            if (diff.TotalDays < 2) return "Yesterday";
            if (diff.TotalDays < 7) return $"{(int)diff.TotalDays} days ago";
            return timestamp.ToString("MMM d, yyyy – h:mm tt");
        }

        private static string DetermineTrendTitle(string? range, DateTime? from, DateTime? to)
        {
            return range switch
            {
                "today" => "Documents Uploaded Today (Hourly)",
                "weekly" => "Documents Uploaded This Week (Daily)",
                "monthly" => "Documents Uploaded This Month (Daily)",
                "yearly" => "Documents Uploaded This Year (Monthly)",
                "custom" when from.HasValue && to.HasValue =>
                    (to.Value - from.Value).TotalDays <= 1 ? "Documents Uploaded (Hourly)" :
                    (to.Value - from.Value).TotalDays <= 14 ? "Documents Uploaded (Daily)" :
                    "Documents Uploaded (Monthly)",
                _ => "Documents Uploaded Per Month"
            };
        }
    }
}
