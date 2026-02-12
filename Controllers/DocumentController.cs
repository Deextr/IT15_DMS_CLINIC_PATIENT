using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DMS_CPMS.Data;
using DMS_CPMS.Data.Models;
using DMS_CPMS.Models.Document;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DMS_CPMS.Controllers
{
    [Authorize(Roles = "SuperAdmin,Admin")]
    public class DocumentController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _environment;

        public DocumentController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IWebHostEnvironment environment)
        {
            _context = context;
            _userManager = userManager;
            _environment = environment;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(AddDocumentViewModel model)
        {
            if (!ModelState.IsValid || model.File == null || model.File.Length == 0 || !IsAllowedFileType(model.File.FileName))
            {
                TempData["Error"] = "Please provide all required document information with a valid file type.";
                return RedirectToAction("Details", "Patient", new { id = model.PatientID });
            }

            var patient = await _context.Patients.FindAsync(model.PatientID);
            if (patient == null)
            {
                return NotFound();
            }

            var currentUserId = _userManager.GetUserId(User);

            var document = new Data.Models.Document
            {
                PatientID = model.PatientID,
                UploadBy = currentUserId ?? string.Empty,
                DocumentTitle = model.DocumentTitle,
                DocumentType = model.DocumentType,
                UploadDate = DateTime.UtcNow
            };

            _context.Documents.Add(document);
            await _context.SaveChangesAsync();

            var relativePath = await SaveFileAsync(model.PatientID, document.DocumentID, 1, model.File);

            var version = new DocumentVersion
            {
                DocumentID = document.DocumentID,
                VersionNumber = 1,
                FilePath = relativePath,
                CreatedDate = DateTime.UtcNow
            };

            _context.DocumentVersions.Add(version);
            await _context.SaveChangesAsync();

            return RedirectToAction("Details", "Patient", new { id = model.PatientID });
        }

        [HttpGet]
        public async Task<IActionResult> ViewDocument(int id, int? versionId = null)
        {
            var document = await _context.Documents
                .Include(d => d.Patient)
                .Include(d => d.UploadedByUser)
                .Include(d => d.Versions)
                .FirstOrDefaultAsync(d => d.DocumentID == id);

            if (document == null)
            {
                return NotFound();
            }

            var versions = document.Versions
                .OrderByDescending(v => v.VersionNumber)
                .ToList();

            DocumentVersion? latestVersion;

            if (versionId.HasValue)
            {
                latestVersion = versions.FirstOrDefault(v => v.VersionID == versionId.Value);
                if (latestVersion == null)
                {
                    latestVersion = versions.FirstOrDefault();
                }
            }
            else
            {
                latestVersion = versions.FirstOrDefault();
            }

            var model = new DocumentDetailsViewModel
            {
                Document = document,
                LatestVersion = latestVersion,
                Versions = versions
            };

            return View("~/Views/Document/ViewDocument.cshtml", model);
        }

        [HttpGet]
        public async Task<IActionResult> DetailsModal(int id, int? versionId = null)
        {
            var document = await _context.Documents
                .Include(d => d.Patient)
                .Include(d => d.UploadedByUser)
                .Include(d => d.Versions)
                .FirstOrDefaultAsync(d => d.DocumentID == id);

            if (document == null)
            {
                return NotFound();
            }

            var versions = document.Versions
                .OrderByDescending(v => v.VersionNumber)
                .ToList();

            DocumentVersion? latestVersion;

            if (versionId.HasValue)
            {
                latestVersion = versions.FirstOrDefault(v => v.VersionID == versionId.Value);
                if (latestVersion == null)
                {
                    latestVersion = versions.FirstOrDefault();
                }
            }
            else
            {
                latestVersion = versions.FirstOrDefault();
            }

            var model = new DocumentDetailsViewModel
            {
                Document = document,
                LatestVersion = latestVersion,
                Versions = versions
            };

            return PartialView("~/Views/Document/_DetailsModalContent.cshtml", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(EditDocumentViewModel model)
        {
            var document = await _context.Documents
                .Include(d => d.Versions)
                .FirstOrDefaultAsync(d => d.DocumentID == model.DocumentID);

            if (document == null)
            {
                return NotFound();
            }

            if (!ModelState.IsValid)
            {
                return RedirectToAction("ViewDocument", new { id = model.DocumentID });
            }

            document.DocumentTitle = model.DocumentTitle;
            document.DocumentType = model.DocumentType;

            _context.Documents.Update(document);
            await _context.SaveChangesAsync();

            if (model.NewFile != null && model.NewFile.Length > 0)
            {
                if (!IsAllowedFileType(model.NewFile.FileName))
                {
                    TempData["Error"] = "The uploaded file type is not allowed.";
                    return RedirectToAction("ViewDocument", new { id = model.DocumentID });
                }

                var currentMaxVersion = document.Versions.Any()
                    ? document.Versions.Max(v => v.VersionNumber)
                    : 0;

                var newVersionNumber = currentMaxVersion + 1;
                var relativePath = await SaveFileAsync(model.PatientID, document.DocumentID, newVersionNumber, model.NewFile);

                var version = new DocumentVersion
                {
                    DocumentID = document.DocumentID,
                    VersionNumber = newVersionNumber,
                    FilePath = relativePath,
                    CreatedDate = DateTime.UtcNow
                };

                _context.DocumentVersions.Add(version);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction("ViewDocument", new { id = model.DocumentID });
        }

        [HttpGet]
        public async Task<IActionResult> Download(int versionId)
        {
            var version = await _context.DocumentVersions
                .Include(v => v.Document)
                .FirstOrDefaultAsync(v => v.VersionID == versionId);

            if (version == null)
            {
                return NotFound();
            }

            var relativePath = version.FilePath;
            var physicalPath = Path.Combine(_environment.WebRootPath, relativePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

            if (!System.IO.File.Exists(physicalPath))
            {
                return NotFound();
            }

            var contentType = GetContentType(physicalPath);
            var fileName = Path.GetFileName(physicalPath);

            var fileBytes = await System.IO.File.ReadAllBytesAsync(physicalPath);
            return File(fileBytes, contentType, fileName);
        }

        private bool IsAllowedFileType(string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            string[] allowed = { ".pdf", ".jpg", ".jpeg", ".png", ".csv" };
            return allowed.Contains(extension);
        }

        private async Task<string> SaveFileAsync(int patientId, int documentId, int versionNumber, Microsoft.AspNetCore.Http.IFormFile file)
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

        private string GetContentType(string path)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();

            return extension switch
            {
                ".pdf" => "application/pdf",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".csv" => "text/csv",
                ".txt" => "text/plain",
                ".doc" => "application/msword",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                _ => "application/octet-stream"
            };
        }
    }
}

