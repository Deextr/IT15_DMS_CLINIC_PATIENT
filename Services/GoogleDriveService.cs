using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;

namespace DMS_CPMS.Services
{
    /// <summary>
    /// Downloads files from Google Drive using a user's OAuth access token.
    /// The token is obtained client-side via Google Identity Services and passed per-request.
    /// No credentials are stored permanently.
    /// </summary>
    public class GoogleDriveService
    {
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<GoogleDriveService> _logger;
        private readonly DocumentConversionService _conversionService;

        private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".jpg", ".jpeg", ".png", ".gif", ".csv", ".txt", ".doc", ".docx", ".xls", ".xlsx"
        };

        // Google Workspace types need export (they don't have a direct binary)
        private static readonly Dictionary<string, (string ExportMime, string Extension)> GoogleMimeExports = new()
        {
            { "application/vnd.google-apps.document", ("application/pdf", ".pdf") },
            { "application/vnd.google-apps.spreadsheet", ("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ".xlsx") },
            { "application/vnd.google-apps.presentation", ("application/pdf", ".pdf") },
        };

        public GoogleDriveService(IConfiguration configuration, IWebHostEnvironment environment, ILogger<GoogleDriveService> logger, DocumentConversionService conversionService)
        {
            _configuration = configuration;
            _environment = environment;
            _logger = logger;
            _conversionService = conversionService;
        }

        /// <summary>
        /// Downloads a file from Google Drive and saves it to system storage.
        /// Uses the same directory structure as local uploads.
        /// </summary>
        public async Task<GoogleDriveDownloadResult> DownloadFileAsync(
            string fileId, string accessToken, int patientId, int documentId, int versionNumber)
        {
            var credential = GoogleCredential.FromAccessToken(accessToken);
            var driveService = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "DMS_CPMS"
            });

            // Get file metadata
            var fileRequest = driveService.Files.Get(fileId);
            fileRequest.Fields = "id, name, mimeType, size";
            var fileMeta = await fileRequest.ExecuteAsync();

            _logger.LogInformation("Google Drive file: Id={FileId}, Name={FileName}, MimeType={MimeType}",
                fileMeta.Id, fileMeta.Name, fileMeta.MimeType);

            string fileName;
            string extension;
            Stream fileStream;

            if (GoogleMimeExports.TryGetValue(fileMeta.MimeType, out var exportInfo))
            {
                // Export Google Workspace file to a standard format
                extension = exportInfo.Extension;
                fileName = Path.GetFileNameWithoutExtension(fileMeta.Name) + extension;

                var exportRequest = driveService.Files.Export(fileId, exportInfo.ExportMime);
                fileStream = new MemoryStream();
                await exportRequest.DownloadAsync(fileStream);
                fileStream.Position = 0;
            }
            else
            {
                // Direct download for regular files
                fileName = fileMeta.Name;
                extension = Path.GetExtension(fileName);

                if (string.IsNullOrEmpty(extension))
                {
                    extension = GetExtensionFromMimeType(fileMeta.MimeType);
                    fileName += extension;
                }

                var downloadRequest = driveService.Files.Get(fileId);
                fileStream = new MemoryStream();
                await downloadRequest.DownloadAsync(fileStream);
                fileStream.Position = 0;
            }

            // Validate file extension (same rules as local upload)
            if (!AllowedExtensions.Contains(extension))
            {
                fileStream.Dispose();
                throw new InvalidOperationException(
                    $"File type '{extension}' is not allowed. Allowed types: {string.Join(", ", AllowedExtensions)}");
            }

            // Save to same directory structure as local uploads
            var uploadsRoot = Path.Combine(_environment.WebRootPath, "uploads", "documents",
                patientId.ToString(), documentId.ToString());
            Directory.CreateDirectory(uploadsRoot);

            var storedFileName = $"v{versionNumber}_{Guid.NewGuid()}{extension}";
            var physicalPath = Path.Combine(uploadsRoot, storedFileName);

            using (var outputStream = new FileStream(physicalPath, FileMode.Create))
            {
                await fileStream.CopyToAsync(outputStream);
            }

            fileStream.Dispose();

            // Generate PDF preview for Word documents
            if (DocumentConversionService.CanConvertToPreview(physicalPath))
            {
                _conversionService.ConvertToPdfPreview(physicalPath);
            }

            var relativePath = $"/uploads/documents/{patientId}/{documentId}/{storedFileName}";

            return new GoogleDriveDownloadResult
            {
                RelativePath = relativePath.Replace("\\", "/"),
                OriginalFileName = fileName,
                Extension = extension,
                GoogleFileId = fileId
            };
        }

        private static string GetExtensionFromMimeType(string mimeType)
        {
            return mimeType switch
            {
                "application/pdf" => ".pdf",
                "image/jpeg" => ".jpg",
                "image/png" => ".png",
                "image/gif" => ".gif",
                "text/csv" => ".csv",
                "text/plain" => ".txt",
                "application/msword" => ".doc",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
                "application/vnd.ms-excel" => ".xls",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ".xlsx",
                _ => ".bin"
            };
        }
    }

    public class GoogleDriveDownloadResult
    {
        public string RelativePath { get; set; } = string.Empty;
        public string OriginalFileName { get; set; } = string.Empty;
        public string Extension { get; set; } = string.Empty;
        public string GoogleFileId { get; set; } = string.Empty;
    }
}
