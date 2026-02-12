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

            // Determine document type (use OtherDocumentType if "Other" is selected)
            var documentType = model.DocumentType == "Other" && !string.IsNullOrEmpty(model.OtherDocumentType)
                ? $"Other - {model.OtherDocumentType}"
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
                .Where(d => d.PatientID == patient.PatientID)
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

            if (!string.IsNullOrWhiteSpace(status))
            {
                // Assuming status is stored somewhere or calculated
                // For now, we'll just filter by active documents
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

            // Determine document type (use OtherDocumentType if "Other" is selected)
            var documentType = model.DocumentType == "Other" && !string.IsNullOrEmpty(model.OtherDocumentType)
                ? $"Other - {model.OtherDocumentType}"
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
                .FirstOrDefaultAsync(d => d.DocumentID == id);

            if (document == null)
            {
                return NotFound();
            }

            var versionsList = document.Versions != null
                ? document.Versions
                    .OrderByDescending(v => v.VersionNumber)
                    .Select(v => new
                    {
                        versionID = v.VersionID,
                        version = $"v{v.VersionNumber}.0",
                        modifiedBy = "Unknown",
                        dateModified = v.CreatedDate.ToString("yyyy-MM-dd"),
                        notes = v.VersionNumber == 1 ? "Initial upload" : $"Version {v.VersionNumber} update"
                    })
                    .Cast<object>()
                    .ToList()
                : new List<object>();

            return Json(new { versions = versionsList });
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
