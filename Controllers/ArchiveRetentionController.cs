using System;
using System.Linq;
using System.Threading.Tasks;
using DMS_CPMS.Data;
using DMS_CPMS.Data.Models;
using DMS_CPMS.Models.Archive;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DMS_CPMS.Controllers
{
    [Authorize(Roles = "SuperAdmin,Admin")]
    public class ArchiveRetentionController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private const int PageSize = 10;

        public ArchiveRetentionController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // ─────────────────────── INDEX (main page) ───────────────────────
        [HttpGet]
        public async Task<IActionResult> Index(string? searchTerm, string? statusFilter, int page = 1, int policyPage = 1)
        {
            var isSuperAdmin = User.IsInRole("SuperAdmin");

            // ── Archived Documents query ──
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
                .Select(a => new ArchivedDocumentViewModel
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

            // ── Stats ──
            var allArchiveCount = await _context.ArchiveDocuments.CountAsync();
            var today2 = DateTime.Today;
            var activeCount = await _context.ArchiveDocuments.CountAsync(a => a.RetentionUntil >= today2);
            var expiredCount = await _context.ArchiveDocuments.CountAsync(a => a.RetentionUntil < today2);

            // ── Retention Policies (paginated) ──
            var totalPolicies = await _context.RetentionPolicies.CountAsync();
            var policyTotalPages = (int)Math.Ceiling(totalPolicies / (double)PageSize);
            policyPage = Math.Max(1, Math.Min(policyPage, Math.Max(1, policyTotalPages)));

            var policies = await _context.RetentionPolicies
                .OrderBy(p => p.ModuleName)
                .Skip((policyPage - 1) * PageSize)
                .Take(PageSize)
                .Select(p => new RetentionPolicyViewModel
                {
                    RetentionPolicyID = p.RetentionPolicyID,
                    ModuleName = p.ModuleName,
                    RetentionDurationMonths = p.RetentionDurationMonths,
                    AutoActionAfterExpiry = p.AutoActionAfterExpiry,
                    IsEnabled = p.IsEnabled
                })
                .ToListAsync();

            // ── Document types for dropdown (standard list) ──
            var documentTypes = new List<string>
            {
                "Medical History",
                "Examination Reports",
                "Lab Reports",
                "Imaging Reports",
                "Prescription Records",
                "Others"
            };

            var model = new ArchiveRetentionIndexViewModel
            {
                ArchivedDocuments = archivedDocs,
                RetentionPolicies = policies,
                TotalDocuments = allArchiveCount,
                ActiveRetentionCount = activeCount,
                ExpiredRetentionCount = expiredCount,
                TotalPolicies = totalPolicies,
                PageNumber = page,
                TotalPages = totalPages,
                PolicyPageNumber = policyPage,
                PolicyTotalPages = policyTotalPages,
                SearchTerm = searchTerm,
                StatusFilter = statusFilter,
                DocumentTypes = documentTypes,
                IsSuperAdmin = isSuperAdmin
            };

            return View("~/Views/ArchiveRetention/Index.cshtml", model);
        }

        // ─────────────────── ARCHIVE A DOCUMENT ──────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ArchiveDocument(ArchiveDocumentFormViewModel form)
        {
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Please fill in all required fields.";
                return RedirectToAction(nameof(Index));
            }

            var document = await _context.Documents.FindAsync(form.DocumentID);
            if (document == null)
            {
                TempData["ErrorMessage"] = "Document not found.";
                return RedirectToAction(nameof(Index));
            }

            if (document.IsArchived)
            {
                TempData["ErrorMessage"] = "Document is already archived.";
                return RedirectToAction(nameof(Index));
            }

            var user = await _userManager.GetUserAsync(User);

            // Look up matching retention policy by document type
            var policy = await _context.RetentionPolicies
                .Where(p => p.IsEnabled && p.ModuleName == document.DocumentType)
                .FirstOrDefaultAsync();

            var archiveDate = DateTime.Today;
            var retentionUntil = policy != null
                ? archiveDate.AddMonths(policy.RetentionDurationMonths)
                : archiveDate.AddYears(5); // default 5 years if no policy

            var archive = new ArchiveDocument
            {
                DocumentID = form.DocumentID,
                UserID = user!.Id,
                ArchiveReason = form.ArchiveReason,
                ArchiveDate = archiveDate,
                RetentionUntil = retentionUntil
            };

            document.IsArchived = true;

            _context.ArchiveDocuments.Add(archive);
            await _context.SaveChangesAsync();

            TempData["StatusMessage"] = $"Document \"{document.DocumentTitle}\" has been archived. Retention until {retentionUntil:MMM dd, yyyy}.";
            return RedirectToAction(nameof(Index));
        }

        // ─────────────────── RESTORE DOCUMENT ────────────────────────────
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
                return RedirectToAction(nameof(Index));
            }

            // Only allow restore if within retention period
            if (archive.RetentionUntil.Date < DateTime.Today)
            {
                TempData["ErrorMessage"] = "Cannot restore — retention period has expired.";
                return RedirectToAction(nameof(Index));
            }

            // Version-level archive: just remove the archive record (version reappears in history)
            if (archive.VersionID != null)
            {
                var docTitle = archive.Document.DocumentTitle;
                _context.ArchiveDocuments.Remove(archive);
                await _context.SaveChangesAsync();

                TempData["StatusMessage"] = $"Version of \"{docTitle}\" has been restored to version history.";
                return RedirectToAction(nameof(Index));
            }

            // Document-level archive: restore the whole document
            if (!archive.Document.IsArchived)
            {
                TempData["ErrorMessage"] = "Document is not currently archived.";
                return RedirectToAction(nameof(Index));
            }

            archive.Document.IsArchived = false;
            _context.ArchiveDocuments.Remove(archive);
            await _context.SaveChangesAsync();

            TempData["StatusMessage"] = $"Document \"{archive.Document.DocumentTitle}\" has been restored to active modules.";
            return RedirectToAction(nameof(Index));
        }

        // ─────────────────── PERMANENT DELETE (SuperAdmin only) ──────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<IActionResult> PermanentDelete(int archiveId)
        {
            var archive = await _context.ArchiveDocuments
                .Include(a => a.Document)
                    .ThenInclude(d => d.Versions)
                .Include(a => a.Document)
                    .ThenInclude(d => d.Patient)
                .FirstOrDefaultAsync(a => a.ArchiveID == archiveId);

            if (archive == null)
            {
                TempData["ErrorMessage"] = "Archive record not found.";
                return RedirectToAction(nameof(Index));
            }

            // Only allow if retention expired
            if (archive.RetentionUntil.Date >= DateTime.Today)
            {
                TempData["ErrorMessage"] = "Cannot permanently delete — retention period has not expired yet.";
                return RedirectToAction(nameof(Index));
            }

            // Version-level archive: delete only the archived version record
            if (archive.VersionID != null)
            {
                var archivedVersion = archive.Document.Versions.FirstOrDefault(v => v.VersionID == archive.VersionID);
                if (archivedVersion != null)
                {
                    var fullPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", archivedVersion.FilePath.TrimStart('/'));
                    if (System.IO.File.Exists(fullPath))
                        System.IO.File.Delete(fullPath);
                    _context.DocumentVersions.Remove(archivedVersion);
                }

                _context.ArchiveDocuments.Remove(archive);
                await _context.SaveChangesAsync();

                TempData["StatusMessage"] = $"Archived version of \"{archive.Document.DocumentTitle}\" has been permanently deleted.";
                return RedirectToAction(nameof(Index));
            }

            var docTitle = archive.Document.DocumentTitle;

            // Document-level archive: delete physical files, all versions, all archive records, and document
            foreach (var version in archive.Document.Versions)
            {
                var fullPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", version.FilePath.TrimStart('/'));
                if (System.IO.File.Exists(fullPath))
                    System.IO.File.Delete(fullPath);
            }

            // Remove all archive records for this document (version-level and document-level)
            var allArchiveRecords = await _context.ArchiveDocuments
                .Where(a => a.DocumentID == archive.DocumentID)
                .ToListAsync();
            _context.ArchiveDocuments.RemoveRange(allArchiveRecords);

            _context.DocumentVersions.RemoveRange(archive.Document.Versions);
            _context.Documents.Remove(archive.Document);
            await _context.SaveChangesAsync();

            TempData["StatusMessage"] = $"Document \"{docTitle}\" has been permanently deleted.";
            return RedirectToAction(nameof(Index));
        }

        // ─────────────────── ADD RETENTION POLICY ────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddPolicy(RetentionPolicyFormViewModel form)
        {
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Please fill in all required fields.";
                return RedirectToAction(nameof(Index));
            }

            // Check duplicate module name
            var exists = await _context.RetentionPolicies
                .AnyAsync(p => p.ModuleName == form.ModuleName);
            if (exists)
            {
                TempData["ErrorMessage"] = $"A retention policy for \"{form.ModuleName}\" already exists.";
                return RedirectToAction(nameof(Index));
            }

            var policy = new RetentionPolicy
            {
                ModuleName = form.ModuleName,
                RetentionDurationMonths = form.RetentionDurationMonths,
                AutoActionAfterExpiry = form.AutoActionAfterExpiry,
                IsEnabled = form.IsEnabled
            };

            _context.RetentionPolicies.Add(policy);
            await _context.SaveChangesAsync();

            TempData["StatusMessage"] = $"Retention policy \"{form.ModuleName}\" has been created.";
            return RedirectToAction(nameof(Index));
        }

        // ─────────────────── EDIT RETENTION POLICY ───────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditPolicy(RetentionPolicyFormViewModel form)
        {
            if (!ModelState.IsValid || form.RetentionPolicyID == null)
            {
                TempData["ErrorMessage"] = "Please fill in all required fields.";
                return RedirectToAction(nameof(Index));
            }

            var policy = await _context.RetentionPolicies.FindAsync(form.RetentionPolicyID.Value);
            if (policy == null)
            {
                TempData["ErrorMessage"] = "Policy not found.";
                return RedirectToAction(nameof(Index));
            }

            // Check duplicate (another policy with same module name)
            var duplicate = await _context.RetentionPolicies
                .AnyAsync(p => p.ModuleName == form.ModuleName && p.RetentionPolicyID != form.RetentionPolicyID.Value);
            if (duplicate)
            {
                TempData["ErrorMessage"] = $"Another policy for \"{form.ModuleName}\" already exists.";
                return RedirectToAction(nameof(Index));
            }

            policy.ModuleName = form.ModuleName;
            policy.RetentionDurationMonths = form.RetentionDurationMonths;
            policy.AutoActionAfterExpiry = form.AutoActionAfterExpiry;
            policy.IsEnabled = form.IsEnabled;

            await _context.SaveChangesAsync();

            TempData["StatusMessage"] = $"Retention policy \"{form.ModuleName}\" has been updated.";
            return RedirectToAction(nameof(Index));
        }

        // ─────────────────── TOGGLE POLICY ───────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TogglePolicy(int id)
        {
            var policy = await _context.RetentionPolicies.FindAsync(id);
            if (policy == null)
            {
                TempData["ErrorMessage"] = "Policy not found.";
                return RedirectToAction(nameof(Index));
            }

            policy.IsEnabled = !policy.IsEnabled;
            await _context.SaveChangesAsync();

            var state = policy.IsEnabled ? "enabled" : "disabled";
            TempData["StatusMessage"] = $"Retention policy \"{policy.ModuleName}\" has been {state}.";
            return RedirectToAction(nameof(Index));
        }

        // ─────────────────── GET ACTIVE DOCUMENTS (for archive modal) ────
        [HttpGet]
        public async Task<IActionResult> GetActiveDocuments(string? term)
        {
            var query = _context.Documents
                .Include(d => d.Patient)
                .Where(d => !d.IsArchived);

            if (!string.IsNullOrWhiteSpace(term))
            {
                var t = term.Trim().ToLower();
                query = query.Where(d =>
                    d.DocumentTitle.ToLower().Contains(t) ||
                    d.Patient.FirstName.ToLower().Contains(t) ||
                    d.Patient.LastName.ToLower().Contains(t));
            }

            var docs = await query
                .OrderByDescending(d => d.UploadDate)
                .Take(50)
                .Select(d => new
                {
                    d.DocumentID,
                    d.DocumentTitle,
                    d.DocumentType,
                    PatientName = d.Patient.FirstName + " " + d.Patient.LastName
                })
                .ToListAsync();

            return Json(docs);
        }
    }
}
