using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DMS_CPMS.Data;
using DMS_CPMS.Data.Models;
using DMS_CPMS.Models.Patient;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DMS_CPMS.Controllers
{
    [Authorize(Roles = "SuperAdmin,Admin,Staff")]
    public class PatientDocumentsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _environment;

        private const int PageSize = 10;

        public PatientDocumentsController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IWebHostEnvironment environment)
        {
            _context = context;
            _userManager = userManager;
            _environment = environment;
        }

        [HttpGet]
        public async Task<IActionResult> Index(int patientId, string? searchTerm, string? documentType, string? status, int page = 1)
        {
            var patient = await _context.Patients.FindAsync(patientId);
            if (patient == null)
            {
                return NotFound();
            }

            var model = await BuildDocumentsViewModel(patient, searchTerm, documentType, status, page);
            return View("~/Views/PatientDocuments/Index.cshtml", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Upload(UploadDocumentViewModel model)
        {
            if (!ModelState.IsValid || model.UploadedFile == null)
            {
                return RedirectToAction(nameof(Index), new { patientId = model.PatientID });
            }

            var patient = await _context.Patients.FindAsync(model.PatientID);
            if (patient == null)
            {
                return NotFound();
            }

            // Determine document type (use OtherDocumentType if "Others" is selected)
            var documentType = model.DocumentType == "Others" && !string.IsNullOrEmpty(model.OtherDocumentType)
                ? $"Others - {model.OtherDocumentType}"
                : model.DocumentType;

            var currentUserId = _userManager.GetUserId(User);
            
            var document = new Document
            {
                PatientID = model.PatientID,
                UploadBy = currentUserId ?? string.Empty,
                DocumentTitle = model.DocumentTitle,
                DocumentType = documentType,
                UploadDate = DateTime.UtcNow
            };

            _context.Documents.Add(document);
            await _context.SaveChangesAsync();

            // Save the file
            var relativePath = await SaveFileAsync(model.PatientID, document.DocumentID, 1, model.UploadedFile);

            // Create initial version
            var version = new DocumentVersion
            {
                DocumentID = document.DocumentID,
                VersionNumber = 1,
                FilePath = relativePath,
                CreatedDate = DateTime.UtcNow
            };

            _context.DocumentVersions.Add(version);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index), new { patientId = model.PatientID });
        }

        private async Task<PatientDocumentsIndexViewModel> BuildDocumentsViewModel(
            Data.Models.Patient patient, string? searchTerm, string? documentType, string? status, int page)
        {
            var query = _context.Documents
                .Where(d => d.PatientID == patient.PatientID && !d.IsArchived)
                .Include(d => d.UploadedByUser)
                .Include(d => d.Versions)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                query = query.Where(d => d.DocumentTitle.Contains(searchTerm));
            }

            if (!string.IsNullOrWhiteSpace(documentType))
            {
                query = query.Where(d => d.DocumentType == documentType);
            }

            var totalCount = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalCount / (double)PageSize);
            page = Math.Max(1, Math.Min(page, totalPages > 0 ? totalPages : 1));

            var documents = await query
                .OrderByDescending(d => d.UploadDate)
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .ToListAsync();

            var documentViewModels = documents.Select(d =>
            {
                var latestVersion = d.Versions?.OrderByDescending(v => v.VersionNumber).FirstOrDefault();
                var filePath = latestVersion?.FilePath ?? string.Empty;
                var extension = !string.IsNullOrEmpty(filePath) ? Path.GetExtension(filePath).ToLower() : string.Empty;
                return new DocumentViewModel
                {
                    DocumentID = d.DocumentID,
                    DocumentName = d.DocumentTitle,
                    DocumentType = d.DocumentType,
                    Version = $"v{d.Versions?.Count ?? 1}.0",
                    UploadedBy = d.UploadedByUser?.UserName ?? "Unknown",
                    UploadDate = d.UploadDate,
                    Status = "Active",
                    FilePath = filePath,
                    FileExtension = extension
                };
            }).ToList();

            return new PatientDocumentsIndexViewModel
            {
                Patient = patient,
                Documents = documentViewModels,
                SearchTerm = searchTerm,
                DocumentTypeFilter = documentType,
                StatusFilter = status,
                PageNumber = page,
                TotalPages = totalPages,
                NewDocument = new UploadDocumentViewModel { PatientID = patient.PatientID }
            };
        }

        [HttpGet]
        public async Task<IActionResult> Download(int id)
        {
            var document = await _context.Documents
                .Include(d => d.Versions)
                .FirstOrDefaultAsync(d => d.DocumentID == id);

            if (document == null)
            {
                return NotFound();
            }

            var latestVersion = document.Versions?.OrderByDescending(v => v.VersionNumber).FirstOrDefault();
            if (latestVersion == null || string.IsNullOrEmpty(latestVersion.FilePath))
            {
                return NotFound("No file found for this document.");
            }

            var physicalPath = Path.Combine(_environment.WebRootPath, latestVersion.FilePath.TrimStart('/'));
            if (!System.IO.File.Exists(physicalPath))
            {
                return NotFound("File not found on server.");
            }

            var mimeType = GetMimeType(Path.GetExtension(physicalPath));
            var fileName = $"{document.DocumentTitle}{Path.GetExtension(physicalPath)}";

            return PhysicalFile(physicalPath, mimeType, fileName);
        }

        [HttpGet]
        public async Task<IActionResult> GetFileInfo(int id)
        {
            var document = await _context.Documents
                .Include(d => d.Versions)
                .FirstOrDefaultAsync(d => d.DocumentID == id);

            if (document == null)
            {
                return NotFound();
            }

            var latestVersion = document.Versions?.OrderByDescending(v => v.VersionNumber).FirstOrDefault();
            if (latestVersion == null || string.IsNullOrEmpty(latestVersion.FilePath))
            {
                return NotFound("No file found for this document.");
            }

            var extension = Path.GetExtension(latestVersion.FilePath).ToLower();
            var isImage = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" }.Contains(extension);
            var isPdf = extension == ".pdf";

            return Json(new
            {
                documentId = document.DocumentID,
                documentName = document.DocumentTitle,
                filePath = latestVersion.FilePath,
                fileExtension = extension,
                isImage = isImage,
                isPdf = isPdf
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(EditDocumentViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return RedirectToAction(nameof(Index), new { patientId = model.DocumentID });
            }

            var document = await _context.Documents.FindAsync(model.DocumentID);
            if (document == null)
            {
                return NotFound();
            }

            // Determine document type (use OtherDocumentType if "Others" is selected)
            var documentType = model.DocumentType == "Others" && !string.IsNullOrEmpty(model.OtherDocumentType)
                ? $"Others - {model.OtherDocumentType}"
                : model.DocumentType;

            document.DocumentTitle = model.DocumentTitle;
            document.DocumentType = documentType;

            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index), new { patientId = document.PatientID });
        }

        [HttpGet]
        public async Task<IActionResult> GetVersionHistory(int id)
        {
            var document = await _context.Documents
                .Include(d => d.Versions)
                .Include(d => d.ArchiveDocuments)
                .FirstOrDefaultAsync(d => d.DocumentID == id);

            if (document == null)
            {
                return NotFound();
            }

            // Get IDs of versions that have been archived
            var archivedVersionIds = document.ArchiveDocuments
                .Where(a => a.VersionID != null)
                .Select(a => a.VersionID!.Value)
                .ToHashSet();

            // Filter out archived versions
            var activeVersions = document.Versions?
                .Where(v => !archivedVersionIds.Contains(v.VersionID))
                .ToList() ?? new List<DocumentVersion>();

            var maxVersion = activeVersions.Any()
                ? activeVersions.Max(v => v.VersionNumber)
                : 0;

            var versionsList = activeVersions
                .OrderByDescending(v => v.VersionNumber)
                .Select(v => new
                {
                    versionID = v.VersionID,
                    version = $"v{v.VersionNumber}.0",
                    modifiedBy = "Unknown",
                    dateModified = v.CreatedDate.ToString("yyyy-MM-dd"),
                    notes = v.VersionNumber == 1 ? "Initial upload" : $"Version {v.VersionNumber} update",
                    isCurrent = v.VersionNumber == maxVersion
                })
                .Cast<object>()
                .ToList();

            return Json(new
            {
                documentId = document.DocumentID,
                documentTitle = document.DocumentTitle,
                isDocumentArchived = document.IsArchived,
                versions = versionsList
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ArchiveDocumentAjax([FromBody] ArchiveDocumentAjaxModel model)
        {
            if (model == null || model.DocumentId <= 0)
            {
                return Json(new { success = false, message = "Invalid request data." });
            }

            var document = await _context.Documents
                .Include(d => d.Patient)
                .FirstOrDefaultAsync(d => d.DocumentID == model.DocumentId);

            if (document == null)
            {
                return Json(new { success = false, message = "Document not found." });
            }

            if (document.IsArchived)
            {
                return Json(new { success = false, message = "Document is already archived." });
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Json(new { success = false, message = "User not found." });
            }

            // Look up matching retention policy by document type
            var policy = await _context.RetentionPolicies
                .Where(p => p.IsEnabled && p.ModuleName == document.DocumentType)
                .FirstOrDefaultAsync();

            var archiveDate = DateTime.Today;
            var retentionUntil = policy != null
                ? archiveDate.AddMonths(policy.RetentionDurationMonths)
                : archiveDate.AddYears(5); // default 5 years if no policy

            var archiveReason = !string.IsNullOrWhiteSpace(model.ArchiveReason)
                ? model.ArchiveReason
                : "Archived from Version History";

            var archive = new ArchiveDocument
            {
                DocumentID = model.DocumentId,
                UserID = user.Id,
                ArchiveReason = archiveReason,
                ArchiveDate = archiveDate,
                RetentionUntil = retentionUntil
            };

            document.IsArchived = true;

            _context.ArchiveDocuments.Add(archive);
            await _context.SaveChangesAsync();

            // Log audit trail
            await LogAuditTrailAsync(
                action: "ArchiveDocument",
                entityType: "Document",
                entityId: document.DocumentID,
                details: $"Document '{document.DocumentTitle}' archived by {user.FirstName} {user.LastName}. Reason: {archiveReason}. Retention until {retentionUntil:MMM dd, yyyy}.",
                userId: user.Id,
                userName: user.UserName ?? "Unknown",
                timestamp: DateTime.UtcNow
            );

            return Json(new
            {
                success = true,
                message = $"Document \"{document.DocumentTitle}\" has been archived. Retention until {retentionUntil:MMM dd, yyyy}."
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "SuperAdmin,Admin,Staff")]
        public async Task<IActionResult> ArchiveVersionAjax([FromBody] ArchiveVersionAjaxModel model)
        {
            if (model == null || model.VersionId <= 0 || model.DocumentId <= 0)
            {
                return Json(new { success = false, message = "Invalid request data." });
            }

            var document = await _context.Documents
                .Include(d => d.Versions)
                .Include(d => d.ArchiveDocuments)
                .FirstOrDefaultAsync(d => d.DocumentID == model.DocumentId);

            if (document == null)
            {
                return Json(new { success = false, message = "Document not found." });
            }

            var version = document.Versions?.FirstOrDefault(v => v.VersionID == model.VersionId);
            if (version == null)
            {
                return Json(new { success = false, message = "Version not found." });
            }

            // Check if already archived
            var alreadyArchived = document.ArchiveDocuments
                .Any(a => a.VersionID == model.VersionId);
            if (alreadyArchived)
            {
                return Json(new { success = false, message = "This version is already archived." });
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Json(new { success = false, message = "User not found." });
            }

            // Retention policy lookup
            var policy = await _context.RetentionPolicies
                .Where(p => p.IsEnabled && p.ModuleName == document.DocumentType)
                .FirstOrDefaultAsync();

            var archiveDate = DateTime.Today;
            var retentionUntil = policy != null
                ? archiveDate.AddMonths(policy.RetentionDurationMonths)
                : archiveDate.AddYears(5);

            var archiveReason = !string.IsNullOrWhiteSpace(model.ArchiveReason)
                ? model.ArchiveReason
                : $"Manually archived version v{version.VersionNumber}.0";

            var archive = new ArchiveDocument
            {
                DocumentID = model.DocumentId,
                VersionID = model.VersionId,
                UserID = user.Id,
                ArchiveReason = archiveReason,
                ArchiveDate = archiveDate,
                RetentionUntil = retentionUntil
            };

            _context.ArchiveDocuments.Add(archive);
            await _context.SaveChangesAsync();

            // Log audit trail
            await LogAuditTrailAsync(
                action: "ArchiveVersion",
                entityType: "DocumentVersion",
                entityId: version.VersionID,
                details: $"Version v{version.VersionNumber}.0 of '{document.DocumentTitle}' archived by {user.FirstName} {user.LastName}. Reason: {archiveReason}. Retention until {retentionUntil:MMM dd, yyyy}.",
                userId: user.Id,
                userName: user.UserName ?? "Unknown",
                timestamp: DateTime.UtcNow
            );

            return Json(new
            {
                success = true,
                message = $"Version v{version.VersionNumber}.0 has been archived. Retention until {retentionUntil:MMM dd, yyyy}."
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetDocuments(int patientId, string? searchTerm, string? documentType, int page = 1, int pageSize = 8)
        {
            // Validate and cap pageSize (max 8)
            pageSize = Math.Min(Math.Max(pageSize, 1), 8);
            
            var patient = await _context.Patients.FindAsync(patientId);
            if (patient == null)
            {
                return Json(new { success = false, message = "Patient not found" });
            }

            var query = _context.Documents
                .Where(d => d.PatientID == patientId && !d.IsArchived)
                .Include(d => d.UploadedByUser)
                .Include(d => d.Versions)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                query = query.Where(d => d.DocumentTitle.Contains(searchTerm));
            }

            if (!string.IsNullOrWhiteSpace(documentType))
            {
                query = query.Where(d => d.DocumentType == documentType);
            }

            var totalCount = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            page = Math.Max(1, Math.Min(page, totalPages > 0 ? totalPages : 1));

            var lastUpload = await query.OrderByDescending(d => d.UploadDate).FirstOrDefaultAsync();

            var documents = await query
                .OrderByDescending(d => d.UploadDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var documentList = documents.Select(d =>
            {
                var latestVersion = d.Versions?.OrderByDescending(v => v.VersionNumber).FirstOrDefault();
                var filePath = latestVersion?.FilePath ?? string.Empty;
                var extension = !string.IsNullOrEmpty(filePath) ? Path.GetExtension(filePath).ToLower() : string.Empty;
                return new
                {
                    documentID = d.DocumentID,
                    documentName = d.DocumentTitle,
                    documentType = d.DocumentType,
                    version = $"v{d.Versions?.Count ?? 1}.0",
                    uploadedBy = d.UploadedByUser?.UserName ?? "Unknown",
                    uploadDate = d.UploadDate.ToString("yyyy-MM-dd"),
                    fileExtension = extension
                };
            }).ToList();

            return Json(new
            {
                success = true,
                documents = documentList,
                pageNumber = page,
                totalPages = totalPages,
                totalCount = totalCount,
                pageSize = pageSize,
                lastUploadDate = lastUpload?.UploadDate.ToString("yyyy-MM-dd") ?? "--"
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadAjax(UploadDocumentViewModel model)
        {
            if (!ModelState.IsValid || model.UploadedFile == null)
            {
                return Json(new { success = false, message = "Invalid input data" });
            }

            var patient = await _context.Patients.FindAsync(model.PatientID);
            if (patient == null)
            {
                return Json(new { success = false, message = "Patient not found" });
            }

            var documentType = model.DocumentType == "Others" && !string.IsNullOrEmpty(model.OtherDocumentType)
                ? $"Others - {model.OtherDocumentType}"
                : model.DocumentType;

            var currentUserId = _userManager.GetUserId(User);
            
            var document = new Document
            {
                PatientID = model.PatientID,
                UploadBy = currentUserId ?? string.Empty,
                DocumentTitle = model.DocumentTitle,
                DocumentType = documentType,
                UploadDate = DateTime.UtcNow
            };

            _context.Documents.Add(document);
            await _context.SaveChangesAsync();

            var relativePath = await SaveFileAsync(model.PatientID, document.DocumentID, 1, model.UploadedFile);

            var version = new DocumentVersion
            {
                DocumentID = document.DocumentID,
                VersionNumber = 1,
                FilePath = relativePath,
                CreatedDate = DateTime.UtcNow
            };

            _context.DocumentVersions.Add(version);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Document uploaded successfully" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditAjax(EditDocumentViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return Json(new { success = false, message = "Invalid input data" });
            }

            var document = await _context.Documents.FindAsync(model.DocumentID);
            if (document == null)
            {
                return Json(new { success = false, message = "Document not found" });
            }

            var documentType = model.DocumentType == "Others" && !string.IsNullOrEmpty(model.OtherDocumentType)
                ? $"Others - {model.OtherDocumentType}"
                : model.DocumentType;

            document.DocumentTitle = model.DocumentTitle;
            document.DocumentType = documentType;

            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Document updated successfully" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditWithVersionAjax([FromForm] EditDocumentWithVersionViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return Json(new { success = false, message = "Invalid input data" });
            }

            var document = await _context.Documents
                .Include(d => d.Versions)
                .FirstOrDefaultAsync(d => d.DocumentID == model.DocumentID);

            if (document == null)
            {
                return Json(new { success = false, message = "Document not found" });
            }

            // Update document metadata
            var documentType = model.DocumentType == "Others" && !string.IsNullOrEmpty(model.OtherDocumentType)
                ? $"Others - {model.OtherDocumentType}"
                : model.DocumentType;

            document.DocumentTitle = model.DocumentTitle;
            document.DocumentType = documentType;
            document.UploadDate = DateTime.UtcNow;

            // Handle new version upload if provided
            if (model.NewVersionFile != null && model.NewVersionFile.Length > 0)
            {
                var latestVersion = document.Versions?.OrderByDescending(v => v.VersionNumber).FirstOrDefault();
                var newVersionNumber = (latestVersion?.VersionNumber ?? 0) + 1;

                var relativePath = await SaveFileAsync(document.PatientID, document.DocumentID, newVersionNumber, model.NewVersionFile);

                var version = new DocumentVersion
                {
                    DocumentID = document.DocumentID,
                    VersionNumber = newVersionNumber,
                    FilePath = relativePath,
                    CreatedDate = DateTime.UtcNow
                };

                _context.DocumentVersions.Add(version);
            }

            await _context.SaveChangesAsync();

            var message = model.NewVersionFile != null && model.NewVersionFile.Length > 0
                ? "Document updated and new version uploaded successfully"
                : "Document updated successfully";

            return Json(new { success = true, message = message });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "SuperAdmin,Admin")]
        public async Task<IActionResult> Delete(int id)
        {
            var document = await _context.Documents
                .Include(d => d.Versions)
                .FirstOrDefaultAsync(d => d.DocumentID == id);

            if (document == null)
            {
                return Json(new { success = false, message = "Document not found" });
            }

            // Delete physical files
            foreach (var version in document.Versions ?? new List<DocumentVersion>())
            {
                if (!string.IsNullOrEmpty(version.FilePath))
                {
                    var physicalPath = Path.Combine(_environment.WebRootPath, version.FilePath.TrimStart('/'));
                    if (System.IO.File.Exists(physicalPath))
                    {
                        System.IO.File.Delete(physicalPath);
                    }
                }
            }

            _context.Documents.Remove(document);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Document deleted successfully" });
        }

        [HttpGet]
        public async Task<IActionResult> DownloadVersion(int versionId)
        {
            var version = await _context.DocumentVersions
                .Include(v => v.Document)
                .FirstOrDefaultAsync(v => v.VersionID == versionId);

            if (version == null || version.Document == null)
            {
                return NotFound("Version not found.");
            }

            if (string.IsNullOrEmpty(version.FilePath))
            {
                return NotFound("No file found for this version.");
            }

            var physicalPath = Path.Combine(_environment.WebRootPath, version.FilePath.TrimStart('/'));
            if (!System.IO.File.Exists(physicalPath))
            {
                return NotFound("File not found on server.");
            }

            var mimeType = GetMimeType(Path.GetExtension(physicalPath));
            var fileName = $"{version.Document.DocumentTitle}_v{version.VersionNumber}{Path.GetExtension(physicalPath)}";

            return PhysicalFile(physicalPath, mimeType, fileName);
        }

        [HttpGet]
        public async Task<IActionResult> GetVersionFileInfo(int versionId)
        {
            var version = await _context.DocumentVersions
                .Include(v => v.Document)
                .FirstOrDefaultAsync(v => v.VersionID == versionId);

            if (version == null)
            {
                return Json(new { success = false, message = "Version not found" });
            }

            var filePath = version.FilePath;
            var extension = !string.IsNullOrEmpty(filePath) ? Path.GetExtension(filePath).ToLower() : string.Empty;
            
            var isImage = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" }.Contains(extension);
            var isPdf = extension == ".pdf";

            return Json(new 
            { 
                success = true, 
                filePath = filePath,
                fileExtension = extension,
                isImage = isImage,
                isPdf = isPdf,
                version = $"v{version.VersionNumber}.0"
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "SuperAdmin,Admin,Staff")]
        public async Task<IActionResult> RestoreVersion([FromBody] RestoreVersionViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return Json(new { success = false, message = "Invalid input data" });
            }

            var document = await _context.Documents
                .Include(d => d.Versions)
                .FirstOrDefaultAsync(d => d.DocumentID == model.DocumentId);

            if (document == null)
            {
                return Json(new { success = false, message = "Document not found" });
            }

            var versionToRestore = document.Versions?.FirstOrDefault(v => v.VersionID == model.VersionId);
            
            if (versionToRestore == null)
            {
                return Json(new { success = false, message = "Version not found" });
            }

            // Check if this is already the current version
            var currentVersion = document.Versions?.OrderByDescending(v => v.VersionNumber).FirstOrDefault();
            if (currentVersion != null && currentVersion.VersionID == model.VersionId)
            {
                return Json(new { success = false, message = "This is already the current version" });
            }

            try
            {
                var currentUserId = _userManager.GetUserId(User);
                var currentUserName = User.Identity?.Name ?? "Unknown";
                var timestamp = DateTime.UtcNow;
                
                // Get the file path of the version to restore
                var sourceFilePath = versionToRestore.FilePath;
                
                if (string.IsNullOrEmpty(sourceFilePath))
                {
                    return Json(new { success = false, message = "Version file path is invalid" });
                }

                var physicalSourcePath = Path.Combine(_environment.WebRootPath, sourceFilePath.TrimStart('/'));
                
                if (!System.IO.File.Exists(physicalSourcePath))
                {
                    return Json(new { success = false, message = "Version file not found on disk" });
                }

                // Calculate new version number
                var newVersionNumber = (currentVersion?.VersionNumber ?? 0) + 1;
                
                // Create new version entry for the restored file
                var extension = Path.GetExtension(sourceFilePath);
                var uploadsRoot = Path.Combine(_environment.WebRootPath, "uploads", "documents", document.PatientID.ToString(), document.DocumentID.ToString());
                Directory.CreateDirectory(uploadsRoot);
                
                var newFileName = $"v{newVersionNumber}_restored_{Guid.NewGuid()}{extension}";
                var physicalNewPath = Path.Combine(uploadsRoot, newFileName);
                
                // Copy the file to create a new version
                System.IO.File.Copy(physicalSourcePath, physicalNewPath);
                
                var newRelativePath = $"/uploads/documents/{document.PatientID}/{document.DocumentID}/{newFileName}".Replace("\\", "/");
                
                // Create new version record
                var newVersion = new DocumentVersion
                {
                    DocumentID = document.DocumentID,
                    VersionNumber = newVersionNumber,
                    FilePath = newRelativePath,
                    CreatedDate = timestamp
                };

                _context.DocumentVersions.Add(newVersion);
                
                // Update document metadata
                document.UploadDate = timestamp;
                
                await _context.SaveChangesAsync();

                // Log to audit trail
                await LogAuditTrailAsync(
                    action: "RestoreVersion",
                    entityType: "DocumentVersion",
                    entityId: newVersion.VersionID,
                    details: $"Restored document '{document.DocumentTitle}' to version {versionToRestore.VersionNumber}. New version {newVersionNumber} created by {currentUserName}.",
                    userId: currentUserId,
                    userName: currentUserName,
                    timestamp: timestamp
                );

                return Json(new 
                { 
                    success = true, 
                    message = "Version restored successfully. The previous version has been preserved as a new historical version.",
                    newVersionNumber = newVersionNumber
                });
            }
            catch (Exception ex)
            {
                // Log the error
                Console.Error.WriteLine($"Error restoring version: {ex.Message}");
                return Json(new { success = false, message = "An error occurred while restoring the version. Please try again." });
            }
        }

        private async Task LogAuditTrailAsync(string action, string entityType, int entityId, string details, string userId, string userName, DateTime timestamp)
        {
            try
            {
                var auditLog = new AuditLog
                {
                    Action = action,
                    EntityType = entityType,
                    EntityId = entityId,
                    Details = details,
                    UserId = userId ?? "Unknown",
                    UserName = userName ?? "Unknown",
                    Timestamp = timestamp,
                    IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown"
                };

                _context.AuditLogs.Add(auditLog);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // Log error but don't fail the main operation
                Console.Error.WriteLine($"Failed to log audit trail: {ex.Message}");
            }
        }

        private static string GetMimeType(string extension)
        {
            return extension.ToLower() switch
            {
                ".pdf" => "application/pdf",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".webp" => "image/webp",
                ".csv" => "text/csv",
                ".txt" => "text/plain",
                ".doc" => "application/msword",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".xls" => "application/vnd.ms-excel",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                _ => "application/octet-stream"
            };
        }

        private async Task<string> SaveFileAsync(int patientId, int documentId, int versionNumber, IFormFile file)
        {
            var uploadsRoot = Path.Combine(_environment.WebRootPath, "uploads", "documents", patientId.ToString(), documentId.ToString());
            Directory.CreateDirectory(uploadsRoot);

            var extension = Path.GetExtension(file.FileName);
            var fileName = $"v{versionNumber}_{Guid.NewGuid()}{extension}";
            var physicalPath = Path.Combine(uploadsRoot, fileName);

            using (var stream = new FileStream(physicalPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            var relativePath = $"/uploads/documents/{patientId}/{documentId}/{fileName}";
            return relativePath.Replace("\\", "/");
        }
    }
}
