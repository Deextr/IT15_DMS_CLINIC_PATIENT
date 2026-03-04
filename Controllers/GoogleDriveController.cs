using System.ComponentModel.DataAnnotations;
using DMS_CPMS.Data;
using DMS_CPMS.Data.Models;
using DMS_CPMS.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace DMS_CPMS.Controllers
{
    /// <summary>
    /// Handles Google Drive Picker uploads. Only SuperAdmin and Admin roles.
    /// </summary>
    [Authorize(Roles = "SuperAdmin,Admin")]
    public class GoogleDriveController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly GoogleDriveService _driveService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<GoogleDriveController> _logger;
        private readonly IAuditLogService _auditLogService;

        public GoogleDriveController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            GoogleDriveService driveService,
            IConfiguration configuration,
            ILogger<GoogleDriveController> logger,
            IAuditLogService auditLogService)
        {
            _context = context;
            _userManager = userManager;
            _driveService = driveService;
            _configuration = configuration;
            _logger = logger;
            _auditLogService = auditLogService;
        }

        /// <summary>
        /// Returns Google API client config needed by the Picker JavaScript.
        /// No secrets exposed — only public client-side keys.
        /// </summary>
        [HttpGet]
        public IActionResult GetConfig()
        {
            var settings = _configuration.GetSection("GoogleDrive");
            return Json(new
            {
                clientId = settings["ClientId"] ?? "",
                apiKey = settings["ApiKey"] ?? "",
                appId = settings["AppId"] ?? ""
            });
        }

        /// <summary>
        /// Uploads a file selected from Google Drive Picker.
        /// Frontend sends the file ID + access token; backend downloads from Drive and stores it.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadFromDrive([FromBody] GoogleDriveUploadRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.FileId) || string.IsNullOrEmpty(request.AccessToken))
                return Json(new { success = false, message = "Invalid request. File ID and access token are required." });

            if (string.IsNullOrEmpty(request.DocumentTitle))
                return Json(new { success = false, message = "Document title is required." });

            if (string.IsNullOrEmpty(request.DocumentType))
                return Json(new { success = false, message = "Document type is required." });

            var patient = await _context.Patients.FindAsync(request.PatientID);
            if (patient == null)
                return Json(new { success = false, message = "Patient not found." });

            try
            {
                var documentType = request.DocumentType == "Others" && !string.IsNullOrEmpty(request.OtherDocumentType)
                    ? $"Others - {request.OtherDocumentType}"
                    : request.DocumentType;

                var currentUserId = _userManager.GetUserId(User);
                var currentUser = await _userManager.GetUserAsync(User);

                // Create document record
                var document = new Document
                {
                    PatientID = request.PatientID,
                    UploadBy = currentUserId ?? string.Empty,
                    DocumentTitle = request.DocumentTitle,
                    DocumentType = documentType,
                    UploadDate = DateTime.UtcNow
                };

                _context.Documents.Add(document);
                await _context.SaveChangesAsync();

                // Download file from Google Drive and save to system storage
                var downloadResult = await _driveService.DownloadFileAsync(
                    request.FileId,
                    request.AccessToken,
                    request.PatientID,
                    document.DocumentID,
                    versionNumber: 1
                );

                // Create initial version (same logic as local upload)
                var version = new DocumentVersion
                {
                    DocumentID = document.DocumentID,
                    VersionNumber = 1,
                    FilePath = downloadResult.RelativePath,
                    CreatedDate = DateTime.UtcNow
                };

                _context.DocumentVersions.Add(version);
                await _context.SaveChangesAsync();

                // Log audit trail
                await _auditLogService.LogAsync("Upload Document (Google Drive)", document.DocumentID);

                _logger.LogInformation(
                    "Document {DocumentId} uploaded from Google Drive by user {UserId}. File: {FileName}",
                    document.DocumentID, currentUserId, downloadResult.OriginalFileName);

                return Json(new { success = true, message = "Document uploaded from Google Drive successfully." });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Invalid file type from Google Drive upload");
                return Json(new { success = false, message = ex.Message });
            }
            catch (Google.GoogleApiException ex)
            {
                _logger.LogError(ex, "Google API error during Drive file download");

                var errorMessage = ex.HttpStatusCode switch
                {
                    System.Net.HttpStatusCode.Unauthorized => "Google Drive authentication expired. Please sign in again.",
                    System.Net.HttpStatusCode.Forbidden => "Permission denied. You don't have access to this file.",
                    System.Net.HttpStatusCode.NotFound => "File not found on Google Drive.",
                    _ => "Failed to download file from Google Drive. Please try again."
                };

                return Json(new { success = false, message = errorMessage });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during Google Drive upload");
                return Json(new { success = false, message = "An unexpected error occurred. Please try again." });
            }
        }

    }

    public class GoogleDriveUploadRequest
    {
        [Required]
        public int PatientID { get; set; }

        [Required]
        [StringLength(100)]
        public string DocumentTitle { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string DocumentType { get; set; } = string.Empty;

        [StringLength(30)]
        public string? OtherDocumentType { get; set; }

        [Required]
        public string FileId { get; set; } = string.Empty;

        [Required]
        public string AccessToken { get; set; } = string.Empty;
    }
}
