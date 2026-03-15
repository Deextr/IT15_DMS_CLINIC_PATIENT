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
using DMS_CPMS.Services;

namespace DMS_CPMS.Areas.Staff.Controllers
{
    [Area("Staff")]
    [Authorize(Roles = "Staff")]
    public class DashboardController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IAuditLogService _auditLogService;
        private const int PageSize = 8;

        public DashboardController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IAuditLogService auditLogService)
        {
            _context = context;
            _userManager = userManager;
            _auditLogService = auditLogService;
        }

        // ─────────────────── DASHBOARD ───────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            var userId = user?.Id ?? "";
            var now = DateTime.Now;
            var today = DateTime.Today;
            var startOfWeek = today.AddDays(-(int)today.DayOfWeek);
            var thirtyDaysAgo = today.AddDays(-30);

            // Simple KPIs for this staff member's workload
            var staffUploadsQuery = _context.AuditLogs
                .Where(a => a.UserID == userId && a.Action == "Upload Document");

            var model = new StaffDashboardViewModel
            {
                StaffFirstName = user?.FirstName ?? "Staff",

                DocumentsUploadedToday = await staffUploadsQuery
                    .Where(a => a.Timestamp >= today)
                    .CountAsync(),

                DocumentsUploadedThisWeek = await staffUploadsQuery
                    .Where(a => a.Timestamp >= startOfWeek)
                    .CountAsync(),

                TotalDocumentsHandled = await _context.AuditLogs
                    .Where(a => a.UserID == userId && a.DocumentID != null)
                    .Select(a => a.DocumentID)
                    .Distinct()
                    .CountAsync(),

                RecentlyArchivedCount = await _context.ArchiveDocuments
                    .Where(a => a.UserID == userId && a.ArchiveDate >= thirtyDaysAgo)
                    .CountAsync(),

                // Recent Activity with human-readable messages
                RecentActivities = await GetRecentActivities(null, null, 10, userId),

                // Notifications
                Notifications = await GetStaffNotifications(userId)
            };

            return View("~/Views/Staff/Dashboard.cshtml", model);
        }

        // ─────────── HELPERS: Dashboard ─────────────────────────────────

        private async Task<List<StaffNotificationItem>> GetStaffNotifications(string userId)
        {
            var notifications = new List<StaffNotificationItem>();
            var today = DateTime.Today;
            var warningThreshold = today.AddDays(7);

            // Documents nearing retention expiration (within 7 days)
            var expiringDocs = await _context.ArchiveDocuments
                .Include(a => a.Document)
                .Where(a => a.UserID == userId
                    && a.RetentionUntil >= today
                    && a.RetentionUntil <= warningThreshold)
                .OrderBy(a => a.RetentionUntil)
                .Take(5)
                .ToListAsync();

            foreach (var doc in expiringDocs)
            {
                var daysLeft = (doc.RetentionUntil - today).Days;
                notifications.Add(new StaffNotificationItem
                {
                    Message = $"\"{doc.Document.DocumentTitle}\" retention expires in {daysLeft} day{(daysLeft != 1 ? "s" : "")}.",
                    Type = daysLeft <= 2 ? "danger" : "warning",
                    Icon = "clock",
                    Date = doc.RetentionUntil
                });
            }

            // Expired retention docs that haven't been addressed
            var expiredCount = await _context.ArchiveDocuments
                .Where(a => a.UserID == userId && a.RetentionUntil < today)
                .CountAsync();

            if (expiredCount > 0)
            {
                notifications.Add(new StaffNotificationItem
                {
                    Message = $"{expiredCount} archived document{(expiredCount != 1 ? "s have" : " has")} expired retention.",
                    Type = "danger",
                    Icon = "alert",
                    Date = today
                });
            }

            return notifications;
        }

        private async Task<List<RecentActivityItem>> GetRecentActivities(DateTime? from, DateTime? to, int count, string userId)
        {
            var query = _context.AuditLogs
                .Where(a => a.UserID == userId && a.DocumentID != null)
                .AsQueryable();

            if (from.HasValue && to.HasValue)
            {
                query = query.Where(a => a.Timestamp >= from.Value && a.Timestamp <= to.Value);
            }

            var logs = await query
                .Include(a => a.Document)
                    .ThenInclude(d => d!.Patient)
                .Include(a => a.User)
                .OrderByDescending(a => a.Timestamp)
                .Take(count)
                .ToListAsync();

            var now = DateTime.Now;
            var activities = new List<RecentActivityItem>();
            foreach (var log in logs)
            {
                var docName = log.Document?.DocumentTitle ?? "Document #" + log.DocumentID;
                var patientName = log.Document?.Patient != null
                    ? $"{log.Document.Patient.FirstName} {log.Document.Patient.LastName}"
                    : "";

                var action = log.Action switch
                {
                    "Upload Document" => "Uploaded",
                    "Edit Document" => "Updated",
                    "Archive Document" => "Archived",
                    "Restore Document" => "Restored",
                    "Restore Version" => "Restored",
                    "Delete Document" => "Deleted",
                    "Download Document" => "Downloaded",
                    _ => log.Action
                };

                // Build human-readable message
                var verb = action.ToLower();
                if (action == "Uploaded") verb = "uploaded";
                else if (action == "Updated") verb = "updated";
                else if (action == "Archived") verb = "archived";
                else if (action == "Restored") verb = "restored";
                else if (action == "Deleted") verb = "deleted";
                else if (action == "Downloaded") verb = "downloaded";

                var humanMsg = !string.IsNullOrEmpty(patientName)
                    ? $"You {verb} {docName} for {patientName}"
                    : $"You {verb} {docName}";

                // Relative time
                var relativeTime = GetRelativeTime(log.Timestamp, now);

                activities.Add(new RecentActivityItem
                {
                    DocumentName = docName,
                    PatientName = patientName,
                    Action = action,
                    Date = log.Timestamp,
                    PerformedBy = "",
                    HumanMessage = humanMsg,
                    RelativeTime = relativeTime
                });
            }

            return activities;
        }

        private static string GetRelativeTime(DateTime date, DateTime now)
        {
            var diff = now - date;
            if (diff.TotalMinutes < 1) return "Just now";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} min ago";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
            if (diff.TotalDays < 2) return "Yesterday";
            if (diff.TotalDays < 7) return $"{(int)diff.TotalDays} days ago";
            return date.ToString("MMM dd, yyyy");
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

                // If the document was marked archived because all versions were archived,
                // clear the flag now that at least one version is being restored.
                if (archive.Document.IsArchived)
                {
                    archive.Document.IsArchived = false;
                }

                await _context.SaveChangesAsync();

                await _auditLogService.LogAsync("Restore Version", archive.DocumentID);

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

            await _auditLogService.LogAsync("Restore Document", archive.DocumentID);

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
            page = Math.Max(1, Math.Min(page, Math.Max(1, totalPages)));

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

