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
    public class PatientController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _environment;

        private const int PageSize = 10;

        public PatientController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IWebHostEnvironment environment)
        {
            _context = context;
            _userManager = userManager;
            _environment = environment;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? searchTerm, string? gender, int page = 1)
        {
            var model = await BuildIndexViewModel(searchTerm, gender, page);
            return View("~/Views/Patient/Index.cshtml", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreatePatientViewModel model)
        {
            if (!ModelState.IsValid)
            {
                var indexModel = await BuildIndexViewModel(null, null, 1);
                indexModel.NewPatient = model;
                return View("~/Views/Patient/Index.cshtml", indexModel);
            }

            var patient = new Data.Models.Patient
            {
                FirstName = model.FirstName,
                LastName = model.LastName,
                BirthDate = model.BirthDate,
                Gender = model.Gender,
                VisitedAt = model.VisitedAt
            };

            _context.Patients.Add(patient);
            await _context.SaveChangesAsync();

            if (model.UploadedFiles != null && model.UploadedFiles.Count > 0)
            {
                var currentUserId = _userManager.GetUserId(User);
                foreach (var file in model.UploadedFiles.Where(f => f != null && f.Length > 0))
                {
                    if (!IsAllowedFileType(file.FileName))
                    {
                        // Skip invalid file types but continue with others
                        continue;
                    }

                    var document = new Document
                    {
                        PatientID = patient.PatientID,
                        UploadBy = currentUserId ?? string.Empty,
                        DocumentTitle = Path.GetFileNameWithoutExtension(file.FileName),
                        DocumentType = Path.GetExtension(file.FileName).Trim('.'),
                        UploadDate = DateTime.UtcNow
                    };

                    _context.Documents.Add(document);
                    await _context.SaveChangesAsync();

                    var relativePath = await SaveFileAsync(patient.PatientID, document.DocumentID, 1, file);

                    var version = new DocumentVersion
                    {
                        DocumentID = document.DocumentID,
                        VersionNumber = 1,
                        FilePath = relativePath,
                        CreatedDate = DateTime.UtcNow
                    };

                    _context.DocumentVersions.Add(version);
                    await _context.SaveChangesAsync();
                }
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var patient = await _context.Patients
                .Include(p => p.Documents)
                .ThenInclude(d => d.Versions)
                .FirstOrDefaultAsync(p => p.PatientID == id);

            if (patient == null)
            {
                return NotFound();
            }

            var documents = patient.Documents
                .Where(d => !d.IsArchived)
                .OrderByDescending(d => d.UploadDate)
                .Select(d => new DocumentSummaryViewModel
                {
                    DocumentID = d.DocumentID,
                    DocumentTitle = d.DocumentTitle,
                    DocumentType = d.DocumentType,
                    UploadDate = d.UploadDate
                })
                .ToList();

            var model = new PatientDetailsViewModel
            {
                Patient = patient,
                Documents = documents
            };

            return View("~/Views/Patient/Details.cshtml", model);
        }

        [HttpGet]
        public async Task<IActionResult> DetailsModal(int id)
        {
            var patient = await _context.Patients
                .Include(p => p.Documents)
                .ThenInclude(d => d.Versions)
                .FirstOrDefaultAsync(p => p.PatientID == id);

            if (patient == null)
            {
                return NotFound();
            }

            var documents = patient.Documents
                .Where(d => !d.IsArchived)
                .OrderByDescending(d => d.UploadDate)
                .Select(d => new DocumentSummaryViewModel
                {
                    DocumentID = d.DocumentID,
                    DocumentTitle = d.DocumentTitle,
                    DocumentType = d.DocumentType,
                    UploadDate = d.UploadDate
                })
                .ToList();

            var model = new PatientDetailsViewModel
            {
                Patient = patient,
                Documents = documents
            };

            return PartialView("~/Views/Patient/_DetailsModalContent.cshtml", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(EditPatientViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return RedirectToAction(nameof(Details), new { id = model.PatientID });
            }

            var patient = await _context.Patients.FindAsync(model.PatientID);
            if (patient == null)
            {
                return NotFound();
            }

            patient.FirstName = model.FirstName;
            patient.LastName = model.LastName;
            patient.BirthDate = model.BirthDate;
            patient.Gender = model.Gender;
            patient.VisitedAt = model.VisitedAt;

            _context.Patients.Update(patient);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Details), new { id = model.PatientID });
        }

        private async Task<PatientIndexViewModel> BuildIndexViewModel(string? searchTerm, string? gender, int page)
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
    }
}

