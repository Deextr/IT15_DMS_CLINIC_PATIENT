using Spire.Doc;

namespace DMS_CPMS.Services
{
    /// <summary>
    /// Converts document files (e.g. .docx, .doc) to PDF for browser preview.
    /// The generated PDF is stored alongside the original file with a _preview.pdf suffix.
    /// </summary>
    public class DocumentConversionService
    {
        private readonly ILogger<DocumentConversionService> _logger;

        private static readonly HashSet<string> ConvertibleExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".doc", ".docx"
        };

        public DocumentConversionService(ILogger<DocumentConversionService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Checks whether the given file extension can be converted to PDF for preview.
        /// </summary>
        public static bool CanConvertToPreview(string filePath)
        {
            var extension = Path.GetExtension(filePath);
            return ConvertibleExtensions.Contains(extension);
        }

        /// <summary>
        /// Gets the preview PDF path for a given file path (convention-based).
        /// Returns the path regardless of whether the file exists.
        /// </summary>
        public static string GetPreviewPath(string filePath)
        {
            var directory = Path.GetDirectoryName(filePath) ?? string.Empty;
            var nameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
            return Path.Combine(directory, $"{nameWithoutExt}_preview.pdf");
        }

        /// <summary>
        /// Gets the web-relative preview path for a stored document path.
        /// </summary>
        public static string GetPreviewRelativePath(string relativePath)
        {
            var directory = Path.GetDirectoryName(relativePath)?.Replace("\\", "/") ?? string.Empty;
            var nameWithoutExt = Path.GetFileNameWithoutExtension(relativePath);
            return $"{directory}/{nameWithoutExt}_preview.pdf";
        }

        /// <summary>
        /// Converts a Word document (.doc/.docx) to PDF for preview.
        /// The PDF is saved alongside the original with a _preview.pdf suffix.
        /// Returns the physical path of the generated PDF, or null if conversion failed.
        /// </summary>
        public string? ConvertToPdfPreview(string physicalFilePath)
        {
            if (!File.Exists(physicalFilePath))
            {
                _logger.LogWarning("Cannot convert file — does not exist: {FilePath}", physicalFilePath);
                return null;
            }

            var extension = Path.GetExtension(physicalFilePath);
            if (!ConvertibleExtensions.Contains(extension))
            {
                _logger.LogDebug("File type {Extension} does not need conversion", extension);
                return null;
            }

            var previewPath = GetPreviewPath(physicalFilePath);

            try
            {
                var document = new Document();
                document.LoadFromFile(physicalFilePath);
                document.SaveToFile(previewPath, FileFormat.PDF);
                document.Close();

                _logger.LogInformation("Successfully converted {Source} to PDF preview at {Preview}",
                    physicalFilePath, previewPath);

                return previewPath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to convert {FilePath} to PDF preview", physicalFilePath);

                // Clean up partial file if it was created
                if (File.Exists(previewPath))
                {
                    try { File.Delete(previewPath); } catch { /* ignore cleanup errors */ }
                }

                return null;
            }
        }

        /// <summary>
        /// Converts a Word document to PDF preview and returns the web-relative preview path.
        /// </summary>
        /// <param name="physicalFilePath">Physical path to the Word document on disk</param>
        /// <param name="relativeFilePath">Web-relative path (e.g. /uploads/documents/1/2/v1_abc.docx)</param>
        /// <returns>Web-relative path to the PDF preview, or null if conversion failed</returns>
        public string? ConvertAndGetRelativePath(string physicalFilePath, string relativeFilePath)
        {
            var result = ConvertToPdfPreview(physicalFilePath);
            if (result == null)
                return null;

            return GetPreviewRelativePath(relativeFilePath);
        }
    }
}
