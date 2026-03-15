using System;
using System.IO;
using System.Threading.Tasks;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DMS_CPMS.Services
{
    /// <summary>
    /// Handles uploading exported report files (PDF / Excel) to AWS S3.
    /// Files are stored under a daily date-prefix folder: yyyy-MM-dd/ReportName_HH-mmtt.ext
    /// No folder is created if no export happens that day.
    /// </summary>
    public interface IS3ExportStorageService
    {
        /// <summary>
        /// Uploads a file to S3 under today's date-prefix folder.
        /// </summary>
        /// <param name="fileBytes">The file content as a byte array.</param>
        /// <param name="reportName">Logical name of the report (e.g. "PatientSummary", "AllReports").</param>
        /// <param name="extension">File extension including the dot (e.g. ".pdf", ".xlsx").</param>
        /// <param name="contentType">MIME type for the file.</param>
        /// <returns>The full S3 object key that was written.</returns>
        Task<string> UploadReportAsync(byte[] fileBytes, string reportName, string extension, string contentType);
    }

    public class S3ExportStorageService : IS3ExportStorageService
    {
        private readonly IAmazonS3 _s3Client;
        private readonly string _bucketName;
        private readonly ILogger<S3ExportStorageService> _logger;

        public S3ExportStorageService(IConfiguration configuration, ILogger<S3ExportStorageService> logger)
        {
            _logger = logger;

            var accessKey = configuration["AWS:AccessKey"]
                ?? throw new InvalidOperationException("AWS:AccessKey is not configured.");
            var secretKey = configuration["AWS:SecretKey"]
                ?? throw new InvalidOperationException("AWS:SecretKey is not configured.");
            var region = configuration["AWS:Region"]
                ?? throw new InvalidOperationException("AWS:Region is not configured.");
            _bucketName = configuration["AWS:S3BucketName"]
                ?? throw new InvalidOperationException("AWS:S3BucketName is not configured.");

            _s3Client = new AmazonS3Client(accessKey, secretKey, RegionEndpoint.GetBySystemName(region));
        }

        public async Task<string> UploadReportAsync(byte[] fileBytes, string reportName, string extension, string contentType)
        {
            // Build the S3 object key: yyyy-MM-dd/ReportName_HH-mmtt.ext
            var now = DateTime.Now;
            var dateFolder = now.ToString("yyyy-MM-dd");
            var timestamp = now.ToString("hh-mmtt"); // e.g. 10-45AM
            var safeReportName = SanitizeFileName(reportName);
            var objectKey = $"{dateFolder}/{safeReportName}_{timestamp}{extension}";

            try
            {
                using var stream = new MemoryStream(fileBytes);
                var putRequest = new PutObjectRequest
                {
                    BucketName = _bucketName,
                    Key = objectKey,
                    InputStream = stream,
                    ContentType = contentType,
                    ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256
                };

                var response = await _s3Client.PutObjectAsync(putRequest);

                _logger.LogInformation(
                    "Successfully uploaded report to S3: {ObjectKey} (HTTP {StatusCode})",
                    objectKey, (int)response.HttpStatusCode);

                return objectKey;
            }
            catch (AmazonS3Exception ex)
            {
                _logger.LogError(ex,
                    "AWS S3 error uploading {ObjectKey} to bucket {Bucket}. " +
                    "StatusCode={StatusCode}, ErrorCode={ErrorCode}",
                    objectKey, _bucketName, ex.StatusCode, ex.ErrorCode);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error uploading {ObjectKey} to S3.", objectKey);
                throw;
            }
        }

        /// <summary>
        /// Removes characters that are unsafe for S3 object keys.
        /// </summary>
        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = string.Join("", name.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c));
            return sanitized.Replace(' ', '_');
        }
    }
}
