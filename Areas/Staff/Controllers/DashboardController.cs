using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DMS_CPMS.Data;
using DMS_CPMS.Data.Models;
using DMS_CPMS.Models.Patient;
using DMS_CPMS.Models.Staff;

namespace DMS_CPMS.Areas.Staff.Controllers
{
    [Area("Staff")]
    [Authorize(Roles = "Staff")]
    public class DashboardController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private const int PageSize = 10;

        public DashboardController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // ─────────────────── DASHBOARD ───────────────────────────────────
        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            var userId = user?.Id ?? "";
            var now = DateTime.Now;
            var startOfMonth = new DateTime(now.Year, now.Month, 1);

            var model = new StaffDashboardViewModel
            {
                StaffFirstName = user?.FirstName ?? "Staff",
                TotalDocuments = await _context.Documents.CountAsync(),
                TotalArchived = await _context.ArchiveDocuments.CountAsync(),
                TotalVersions = await _context.DocumentVersions.CountAsync(),
                DocumentsUploadedThisMonth = await _context.Documents
                    .Where(d => d.UploadDate >= startOfMonth)
                    .CountAsync(),

                // Active vs Archived
                ActiveDocumentCount = await _context.Documents.CountAsync(d => !d.IsArchived),
                ArchivedDocumentCount = await _context.Documents.CountAsync(d => d.IsArchived),

                // Monthly Upload Trend (last 6 months — "all time" default = monthly)
                MonthlyUploadTrend = await GetMonthlyUploadTrend(now.AddMonths(-5), now),

                // Document Type Distribution
                DocumentTypeDistribution = await _context.Documents
                    .GroupBy(d => d.DocumentType)
                    .Select(g => new DocumentTypeDataPoint
                    {
                        DocumentType = g.Key,
                        Count = g.Count()
                    })
                    .OrderByDescending(x => x.Count)
                    .ToListAsync(),

                // Recent Activity — filtered to this staff user only
                RecentActivities = await GetRecentActivities(null, null, 10, userId)
            };

            return View("~/Views/Staff/Dashboard.cshtml", model);
        }

        // ─────────────────── AJAX: DASHBOARD DATA ────────────────────────
        [HttpGet]
        public async Task<IActionResult> DashboardData(string? range, string? startDate, string? endDate)
        {
            var user = await _userManager.GetUserAsync(User);
            var userId = user?.Id ?? "";

            DateTime? from = null;
            DateTime? to = null;
            var now = DateTime.Now;

            switch (range)
            {
                case "today":
                    from = DateTime.Today;
                    to = now;
                    break;
                case "7days":
                    from = DateTime.Today.AddDays(-7);
                    to = now;
                    break;
                case "30days":
                    from = DateTime.Today.AddDays(-30);
                    to = now;
                    break;
                case "custom":
                    if (DateTime.TryParse(startDate, out var s) && DateTime.TryParse(endDate, out var e))
                    {
                        from = s.Date;
                        to = e.Date.AddDays(1).AddSeconds(-1); // end of day
                    }
                    break;
            }

            // KPIs
            var docsQuery = _context.Documents.AsQueryable();
            var archiveQuery = _context.ArchiveDocuments.AsQueryable();
            var versionsQuery = _context.DocumentVersions.AsQueryable();

            if (from.HasValue && to.HasValue)
            {
                docsQuery = docsQuery.Where(d => d.UploadDate >= from.Value && d.UploadDate <= to.Value);
                archiveQuery = archiveQuery.Where(a => a.ArchiveDate >= from.Value && a.ArchiveDate <= to.Value);
                versionsQuery = versionsQuery.Where(v => v.CreatedDate >= from.Value && v.CreatedDate <= to.Value);
            }

            var totalDocs = await docsQuery.CountAsync();
            var totalArchived = await archiveQuery.CountAsync();
            var totalVersions = await versionsQuery.CountAsync();
            var docsThisMonth = await docsQuery
                .Where(d => d.UploadDate >= new DateTime(now.Year, now.Month, 1))
                .CountAsync();

            if (from.HasValue && to.HasValue)
            {
                docsThisMonth = totalDocs;
            }

            // Active vs Archived (global)
            var activeDocs = await _context.Documents.CountAsync(d => !d.IsArchived);
            var archivedDocs = await _context.Documents.CountAsync(d => d.IsArchived);

            // ── Dynamic Trend Grouping ──
            var trendData = await GetDynamicUploadTrend(range, from, to, now);

            // Document Type Distribution
            var typeDistro = await (from.HasValue && to.HasValue
                ? _context.Documents
                    .Where(d => d.UploadDate >= from.Value && d.UploadDate <= to.Value)
                : _context.Documents)
                .GroupBy(d => d.DocumentType)
                .Select(g => new DocumentTypeDataPoint
                {
                    DocumentType = g.Key,
                    Count = g.Count()
                })
                .OrderByDescending(x => x.Count)
                .ToListAsync();

            // Recent Activity — filtered to this staff only
            var activities = await GetRecentActivities(from, to, 10, userId);

            return Json(new StaffDashboardDataResponse
            {
                TotalDocuments = totalDocs,
                TotalArchived = totalArchived,
                TotalVersions = totalVersions,
                DocumentsUploadedThisMonth = docsThisMonth,
                ActiveDocumentCount = activeDocs,
                ArchivedDocumentCount = archivedDocs,
                MonthlyUploadTrend = trendData.Points,
                TrendTitle = trendData.Title,
                DocumentTypeDistribution = typeDistro,
                RecentActivities = activities
            });
        }

        // ─────────── HELPERS: Dashboard aggregations ─────────────────────

        /// <summary>
        /// Dynamic trend grouping: hourly / daily / monthly depending on range.
        /// </summary>
        private async Task<(List<MonthlyUploadDataPoint> Points, string Title)> GetDynamicUploadTrend(
            string? range, DateTime? from, DateTime? to, DateTime now)
        {
            // Determine grouping mode
            string mode; // "hourly", "daily", "monthly"
            DateTime rangeStart, rangeEnd;

            switch (range)
            {
                case "today":
                    mode = "hourly";
                    rangeStart = DateTime.Today;
                    rangeEnd = now;
                    break;
                case "7days":
                    mode = "daily";
                    rangeStart = DateTime.Today.AddDays(-6); // include today
                    rangeEnd = now;
                    break;
                case "30days":
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
                        // fallback
                        rangeStart = now.AddMonths(-5);
                        rangeEnd = now;
                        mode = "monthly";
                    }
                    break;
                default: // "all" or null
                    rangeStart = now.AddMonths(-5);
                    rangeEnd = now;
                    mode = "monthly";
                    break;
            }

            return mode switch
            {
                "hourly" => (await GetHourlyTrend(rangeStart, rangeEnd), "Hourly Document Upload Trend"),
                "daily" => (await GetDailyTrend(rangeStart, rangeEnd), "Daily Document Upload Trend"),
                _ => (await GetMonthlyUploadTrend(rangeStart, rangeEnd), "Monthly Document Upload Trend")
            };
        }

        private async Task<List<MonthlyUploadDataPoint>> GetHourlyTrend(DateTime from, DateTime to)
        {
            var uploads = await _context.Documents
                .Where(d => d.UploadDate >= from && d.UploadDate <= to)
                .GroupBy(d => d.UploadDate.Hour)
                .Select(g => new { Hour = g.Key, Count = g.Count() })
                .ToListAsync();

            var result = new List<MonthlyUploadDataPoint>();
            for (int h = 0; h < 24; h++)
            {
                var match = uploads.FirstOrDefault(u => u.Hour == h);
                var ampm = h < 12 ? "AM" : "PM";
                var hour12 = h == 0 ? 12 : h > 12 ? h - 12 : h;
                result.Add(new MonthlyUploadDataPoint
                {
                    Label = $"{hour12} {ampm}",
                    Count = match?.Count ?? 0
                });
            }
            return result;
        }

        private async Task<List<MonthlyUploadDataPoint>> GetDailyTrend(DateTime from, DateTime to)
        {
            var startDate = from.Date;
            var endDate = to.Date;

            var uploads = await _context.Documents
                .Where(d => d.UploadDate >= startDate && d.UploadDate <= to)
                .GroupBy(d => d.UploadDate.Date)
                .Select(g => new { Date = g.Key, Count = g.Count() })
                .ToListAsync();

            var result = new List<MonthlyUploadDataPoint>();
            var current = startDate;
            while (current <= endDate)
            {
                var match = uploads.FirstOrDefault(u => u.Date == current);
                result.Add(new MonthlyUploadDataPoint
                {
                    Label = current.ToString("MMM dd"),
                    Count = match?.Count ?? 0
                });
                current = current.AddDays(1);
            }
            return result;
        }

        private async Task<List<MonthlyUploadDataPoint>> GetMonthlyUploadTrend(DateTime from, DateTime to)
        {
            var startMonth = new DateTime(from.Year, from.Month, 1);
            var endMonth = new DateTime(to.Year, to.Month, 1);

            var uploads = await _context.Documents
                .Where(d => d.UploadDate >= startMonth && d.UploadDate <= to)
                .GroupBy(d => new { d.UploadDate.Year, d.UploadDate.Month })
                .Select(g => new
                {
                    g.Key.Year,
                    g.Key.Month,
                    Count = g.Count()
                })
                .ToListAsync();

            var result = new List<MonthlyUploadDataPoint>();
            var current = startMonth;
            while (current <= endMonth)
            {
                var match = uploads.FirstOrDefault(u => u.Year == current.Year && u.Month == current.Month);
                result.Add(new MonthlyUploadDataPoint
                {
                    Label = current.ToString("MMM yyyy"),
                    Count = match?.Count ?? 0
                });
                current = current.AddMonths(1);
            }

            return result;
        }

        private async Task<List<RecentActivityItem>> GetRecentActivities(DateTime? from, DateTime? to, int count, string userId)
        {
            var query = _context.AuditLogs
                .Where(a => a.EntityType == "Document" || a.EntityType == "DocumentVersion" || a.EntityType == "ArchiveDocument")
                .Where(a => a.UserId == userId)
                .AsQueryable();

            if (from.HasValue && to.HasValue)
            {
                query = query.Where(a => a.Timestamp >= from.Value && a.Timestamp <= to.Value);
            }

            var logs = await query
                .OrderByDescending(a => a.Timestamp)
                .Take(count)
                .ToListAsync();

            var activities = new List<RecentActivityItem>();
            foreach (var log in logs)
            {
                // Parse document/patient info from details
                var docName = ExtractBetween(log.Details, "\"", "\"") ?? "Document #" + log.EntityId;
                var patientName = "";

                // Try to get patient name from the document
                if (log.EntityType == "Document" || log.EntityType == "DocumentVersion")
                {
                    var doc = await _context.Documents
                        .Include(d => d.Patient)
                        .FirstOrDefaultAsync(d => d.DocumentID == log.EntityId);
                    if (doc != null)
                    {
                        docName = doc.DocumentTitle;
                        patientName = doc.Patient.FirstName + " " + doc.Patient.LastName;
                    }
                }
                else if (log.EntityType == "ArchiveDocument")
                {
                    var archive = await _context.ArchiveDocuments
                        .Include(a => a.Document).ThenInclude(d => d.Patient)
                        .FirstOrDefaultAsync(a => a.ArchiveID == log.EntityId);
                    if (archive != null)
                    {
                        docName = archive.Document.DocumentTitle;
                        patientName = archive.Document.Patient.FirstName + " " + archive.Document.Patient.LastName;
                    }
                }

                var action = log.Action switch
                {
                    "Create" => "Uploaded",
                    "Upload" => "Uploaded",
                    "Update" => "Updated",
                    "Archive" => "Archived",
                    "Restore" => "Restored",
                    "Delete" => "Deleted",
                    _ => log.Action
                };

                activities.Add(new RecentActivityItem
                {
                    DocumentName = docName,
                    PatientName = patientName,
                    Action = action,
                    Date = log.Timestamp,
                    PerformedBy = log.UserName
                });
            }

            return activities;
        }

        private static string? ExtractBetween(string source, string start, string end)
        {
            var startIdx = source.IndexOf(start);
            if (startIdx < 0) return null;
            startIdx += start.Length;
            var endIdx = source.IndexOf(end, startIdx);
            if (endIdx < 0) return null;
            return source[startIdx..endIdx];
        }

        public async Task<IActionResult> Patients(string? searchTerm, string? gender, int page = 1)
        {
            var model = await BuildPatientIndexViewModel(searchTerm, gender, page);
            return View("~/Views/Staff/Patients.cshtml", model);
        }

        // ─────────────────── ARCHIVE INDEX ───────────────────────────────
        [HttpGet]
        public async Task<IActionResult> ArchiveRetention(string? searchTerm, string? statusFilter, int page = 1)
        {
            var archiveQuery = _context.ArchiveDocuments
                .Include(a => a.Document).ThenInclude(d => d.Patient)
                .Include(a => a.ArchivedByUser)
                .Include(a => a.ArchivedVersion)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();
                archiveQuery = archiveQuery.Where(a =>
                    a.Document.DocumentTitle.ToLower().Contains(term) ||
                    a.ArchiveReason.ToLower().Contains(term) ||
                    a.ArchivedByUser.FirstName.ToLower().Contains(term) ||
                    a.ArchivedByUser.LastName.ToLower().Contains(term));
            }

            if (!string.IsNullOrWhiteSpace(statusFilter))
            {
                var today = DateTime.Today;
                archiveQuery = statusFilter switch
                {
                    "Active" => archiveQuery.Where(a => a.RetentionUntil >= today),
                    "Expired" => archiveQuery.Where(a => a.RetentionUntil < today),
                    _ => archiveQuery
                };
            }

            var totalArchived = await archiveQuery.CountAsync();
            var totalPages = (int)Math.Ceiling(totalArchived / (double)PageSize);
            page = Math.Max(1, Math.Min(page, Math.Max(1, totalPages)));

            var archivedDocs = await archiveQuery
                .OrderByDescending(a => a.ArchiveDate)
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .Select(a => new StaffArchivedDocumentViewModel
                {
                    ArchiveID = a.ArchiveID,
                    DocumentID = a.DocumentID,
                    DocumentTitle = a.Document.DocumentTitle,
                    DocumentType = a.Document.DocumentType,
                    PatientName = a.Document.Patient.FirstName + " " + a.Document.Patient.LastName,
                    ArchivedByName = a.ArchivedByUser.FirstName + " " + a.ArchivedByUser.LastName,
                    ArchiveReason = a.ArchiveReason,
                    ArchiveDate = a.ArchiveDate,
                    RetentionUntil = a.RetentionUntil,
                    VersionNumber = a.ArchivedVersion != null ? a.ArchivedVersion.VersionNumber : null
                })
                .ToListAsync();

            // Stats
            var today2 = DateTime.Today;
            var allArchiveCount = await _context.ArchiveDocuments.CountAsync();
            var activeCount = await _context.ArchiveDocuments.CountAsync(a => a.RetentionUntil >= today2);
            var expiredCount = await _context.ArchiveDocuments.CountAsync(a => a.RetentionUntil < today2);

            var model = new StaffArchiveIndexViewModel
            {
                ArchivedDocuments = archivedDocs,
                TotalDocuments = allArchiveCount,
                ActiveRetentionCount = activeCount,
                ExpiredRetentionCount = expiredCount,
                PageNumber = page,
                TotalPages = totalPages,
                SearchTerm = searchTerm,
                StatusFilter = statusFilter
            };

            return View("~/Views/Staff/ArchiveRetention.cshtml", model);
        }

        // ─────────────────── RESTORE DOCUMENT (Staff) ────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreDocument(int archiveId)
        {
            var archive = await _context.ArchiveDocuments
                .Include(a => a.Document)
                .FirstOrDefaultAsync(a => a.ArchiveID == archiveId);

            if (archive == null)
            {
                TempData["ErrorMessage"] = "Archive record not found.";
                return RedirectToAction(nameof(ArchiveRetention));
            }

            // Only allow restore if within retention period
            if (archive.RetentionUntil.Date < DateTime.Today)
            {
                TempData["ErrorMessage"] = "Cannot restore — retention period has expired.";
                return RedirectToAction(nameof(ArchiveRetention));
            }

            // Version-level archive: just remove the archive record (version reappears in history)
            if (archive.VersionID != null)
            {
                var docTitle = archive.Document.DocumentTitle;
                _context.ArchiveDocuments.Remove(archive);
                await _context.SaveChangesAsync();

                TempData["StatusMessage"] = $"Version of \"{docTitle}\" has been restored to version history.";
                return RedirectToAction(nameof(ArchiveRetention));
            }

            // Document-level archive: restore the whole document
            if (!archive.Document.IsArchived)
            {
                TempData["ErrorMessage"] = "Document is not currently archived.";
                return RedirectToAction(nameof(ArchiveRetention));
            }

            archive.Document.IsArchived = false;
            _context.ArchiveDocuments.Remove(archive);
            await _context.SaveChangesAsync();

            TempData["StatusMessage"] = $"Document \"{archive.Document.DocumentTitle}\" has been restored to active modules.";
            return RedirectToAction(nameof(ArchiveRetention));
        }

        // ─────────────────── HELPERS ─────────────────────────────────────
        private async Task<PatientIndexViewModel> BuildPatientIndexViewModel(string? searchTerm, string? gender, int page)
        {
            var query = _context.Patients.AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                query = query.Where(p =>
                    p.FirstName.Contains(searchTerm) || p.LastName.Contains(searchTerm));
            }

            if (!string.IsNullOrWhiteSpace(gender))
            {
                query = query.Where(p => p.Gender == gender);
            }

            var totalCount = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalCount / (double)PageSize);
            page = Math.Max(1, page);

            var patients = await query
                .OrderBy(p => p.LastName)
                .ThenBy(p => p.FirstName)
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .ToListAsync();

            return new PatientIndexViewModel
            {
                Patients = patients,
                SearchTerm = searchTerm,
                GenderFilter = gender,
                PageNumber = page,
                TotalPages = totalPages
            };
        }
    }
}

