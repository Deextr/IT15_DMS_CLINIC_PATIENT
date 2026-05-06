using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DMS_CPMS.Data;
using DMS_CPMS.Data.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;

namespace DMS_CPMS.Services.BackupRecovery
{
    public interface IBackupRecoveryService
    {
        Task<SystemBackup> CreateBackupAsync(string? initiatedByUserId, string? initiatedByRole, CancellationToken cancellationToken);
        Task RestoreFromExistingBackupAsync(int systemBackupId, string initiatedByUserId, string? initiatedByRole, CancellationToken cancellationToken);
        Task<SystemBackup> SaveUploadedBackupAndRestoreAsync(Stream uploadedEncryptedFile, string originalFileName, long contentLength, string initiatedByUserId, string? initiatedByRole, CancellationToken cancellationToken);
        Task<IReadOnlyList<SystemBackup>> GetHistoryAsync(int take = 50, CancellationToken cancellationToken = default);
    }

    public sealed class BackupRecoveryService : IBackupRecoveryService
    {
        private const string BackupFilePrefix = "clinixdocs_backup_";
        private static readonly SemaphoreSlim InProcessGate = new(1, 1);

        private readonly ApplicationDbContext _db;
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _env;
        private readonly IBackupCryptoService _crypto;
        private readonly IAuditLogService _audit;
        private readonly ILogger<BackupRecoveryService> _logger;

        public BackupRecoveryService(
            ApplicationDbContext db,
            IConfiguration config,
            IWebHostEnvironment env,
            IBackupCryptoService crypto,
            IAuditLogService audit,
            ILogger<BackupRecoveryService> logger)
        {
            _db = db;
            _config = config;
            _env = env;
            _crypto = crypto;
            _audit = audit;
            _logger = logger;
        }

        public async Task<IReadOnlyList<SystemBackup>> GetHistoryAsync(int take = 50, CancellationToken cancellationToken = default)
        {
            take = Math.Clamp(take, 1, 500);
            return await _db.SystemBackups
                .OrderByDescending(b => b.CreatedUtc)
                .Take(take)
                .AsNoTracking()
                .ToListAsync(cancellationToken);
        }

        public async Task<SystemBackup> CreateBackupAsync(string? initiatedByUserId, string? initiatedByRole, CancellationToken cancellationToken)
        {
            await InProcessGate.WaitAsync(cancellationToken);
            try
            {
                await using var appLock = await AcquireDbAppLockAsync(cancellationToken);

                var nowUtc = DateTime.UtcNow;
                var stamp = nowUtc.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                var fileName = $"{BackupFilePrefix}{stamp}{_crypto.FileExtension}";
                var backupsDir = GetLocalBackupsDirectory();
                var outputPath = Path.Combine(backupsDir, fileName);

                var masterKey = GetEncryptionKey();

                // Build plaintext zip into a temp file first to keep encryption streaming-friendly.
                var tempZipPath = Path.Combine(Path.GetTempPath(), $"clinixdocs_backup_{Guid.NewGuid():N}.zip");
                try
                {
                    await CreatePlainBackupZipAsync(tempZipPath, cancellationToken);

                    await using (var zipStream = new FileStream(tempZipPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        _crypto.EncryptToFile(zipStream, outputPath, masterKey);
                    }

                    var fi = new FileInfo(outputPath);
                    var record = new SystemBackup
                    {
                        FileName = fileName,
                        StorageProvider = "Local",
                        StoragePath = outputPath,
                        SizeBytes = fi.Length,
                        CreatedUtc = nowUtc,
                        CreatedByUserId = initiatedByUserId,
                        CreatedByRole = initiatedByRole
                    };

                    _db.SystemBackups.Add(record);
                    await _db.SaveChangesAsync(cancellationToken);

                    if (!string.IsNullOrWhiteSpace(initiatedByUserId))
                        await _audit.LogAsync("System Backup Created", initiatedByUserId, details: fileName);
                    else
                        await _audit.LogAsync("System Backup Created", details: fileName);
                    return record;
                }
                finally
                {
                    TryDelete(tempZipPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Backup creation failed.");
                if (!string.IsNullOrWhiteSpace(initiatedByUserId))
                    await _audit.LogAsync("System Backup Failed", initiatedByUserId, details: ex.Message);
                else
                    await _audit.LogAsync("System Backup Failed", details: ex.Message);
                throw;
            }
            finally
            {
                InProcessGate.Release();
            }
        }

        public async Task RestoreFromExistingBackupAsync(int systemBackupId, string initiatedByUserId, string? initiatedByRole, CancellationToken cancellationToken)
        {
            await InProcessGate.WaitAsync(cancellationToken);
            try
            {
                await using var appLock = await AcquireDbAppLockAsync(cancellationToken);

                var backup = await _db.SystemBackups.FirstOrDefaultAsync(b => b.SystemBackupId == systemBackupId, cancellationToken);
                if (backup == null) throw new InvalidOperationException("Backup record not found.");
                if (!string.Equals(backup.StorageProvider, "Local", StringComparison.OrdinalIgnoreCase))
                    throw new NotSupportedException("Only local backups are supported by this deployment.");
                if (!File.Exists(backup.StoragePath)) throw new FileNotFoundException("Backup file is missing from storage.", backup.StoragePath);

                // Automatic pre-restore backup
                var pre = await CreateBackupAsync(initiatedByUserId, initiatedByRole, cancellationToken);
                try
                {
                    await RestoreFromEncryptedFileAsync(backup.StoragePath, initiatedByUserId, cancellationToken);
                }
                catch (Exception restoreEx)
                {
                    _logger.LogError(restoreEx, "Restore failed; attempting fail-safe rollback to pre-restore backup {PreBackup}.", pre.FileName);
                    try
                    {
                        await RestoreFromEncryptedFileAsync(pre.StoragePath, initiatedByUserId, cancellationToken);
                        await _audit.LogAsync("System Restore Rollback Completed", initiatedByUserId, details: pre.FileName);
                    }
                    catch (Exception rollbackEx)
                    {
                        _logger.LogError(rollbackEx, "Rollback restore to pre-restore backup also failed.");
                        await _audit.LogAsync("System Restore Rollback Failed", initiatedByUserId, details: rollbackEx.Message);
                    }

                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Restore failed.");
                await _audit.LogAsync("System Restore Failed", initiatedByUserId, details: ex.Message);
                throw;
            }
            finally
            {
                InProcessGate.Release();
            }
        }

        public async Task<SystemBackup> SaveUploadedBackupAndRestoreAsync(Stream uploadedEncryptedFile, string originalFileName, long contentLength, string initiatedByUserId, string? initiatedByRole, CancellationToken cancellationToken)
        {
            await InProcessGate.WaitAsync(cancellationToken);
            try
            {
                await using var appLock = await AcquireDbAppLockAsync(cancellationToken);

                if (contentLength <= 0) throw new InvalidOperationException("Uploaded file is empty.");
                var maxBytes = _config.GetValue<long?>("BackupRecovery:MaxUploadBytes") ?? (1024L * 1024L * 1024L * 5L); // 5GB default
                if (contentLength > maxBytes) throw new InvalidOperationException("Uploaded backup file is too large.");

                var ext = Path.GetExtension(originalFileName ?? string.Empty);
                if (!string.Equals(ext, _crypto.FileExtension, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Only encrypted backup files (.enc) are accepted.");

                var nowUtc = DateTime.UtcNow;
                var stamp = nowUtc.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                var fileName = $"{BackupFilePrefix}{stamp}{_crypto.FileExtension}";
                var backupsDir = GetLocalBackupsDirectory();
                var savedPath = Path.Combine(backupsDir, fileName);

                Directory.CreateDirectory(backupsDir);
                await using (var fs = new FileStream(savedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    await uploadedEncryptedFile.CopyToAsync(fs, cancellationToken);
                }

                // Validate header + integrity before accepting into history
                _ = _crypto.ReadHeader(savedPath);
                using (var _ = _crypto.DecryptToStream(savedPath, GetEncryptionKey()))
                {
                    // validate HMAC + decryptability
                }

                var fi = new FileInfo(savedPath);
                var record = new SystemBackup
                {
                    FileName = fileName,
                    StorageProvider = "Local",
                    StoragePath = savedPath,
                    SizeBytes = fi.Length,
                    CreatedUtc = nowUtc,
                    CreatedByUserId = initiatedByUserId,
                    CreatedByRole = initiatedByRole,
                    Notes = "Uploaded external backup"
                };
                _db.SystemBackups.Add(record);
                await _db.SaveChangesAsync(cancellationToken);

                // Automatic pre-restore backup
                var pre = await CreateBackupAsync(initiatedByUserId, initiatedByRole, cancellationToken);
                try
                {
                    await RestoreFromEncryptedFileAsync(savedPath, initiatedByUserId, cancellationToken);
                }
                catch (Exception restoreEx)
                {
                    _logger.LogError(restoreEx, "Uploaded restore failed; attempting fail-safe rollback to pre-restore backup {PreBackup}.", pre.FileName);
                    try
                    {
                        await RestoreFromEncryptedFileAsync(pre.StoragePath, initiatedByUserId, cancellationToken);
                        await _audit.LogAsync("System Restore Rollback Completed", initiatedByUserId, details: pre.FileName);
                    }
                    catch (Exception rollbackEx)
                    {
                        _logger.LogError(rollbackEx, "Rollback restore to pre-restore backup also failed.");
                        await _audit.LogAsync("System Restore Rollback Failed", initiatedByUserId, details: rollbackEx.Message);
                    }

                    throw;
                }
                return record;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Uploaded restore failed.");
                await _audit.LogAsync("System Restore (Upload) Failed", initiatedByUserId, details: ex.Message);
                throw;
            }
            finally
            {
                InProcessGate.Release();
            }
        }

        private async Task RestoreFromEncryptedFileAsync(string encryptedPath, string initiatedByUserId, CancellationToken cancellationToken)
        {
            var masterKey = GetEncryptionKey();
            var tempZipPath = Path.Combine(Path.GetTempPath(), $"clinixdocs_restore_{Guid.NewGuid():N}.zip");
            var tempExtractDir = Path.Combine(Path.GetTempPath(), $"clinixdocs_restore_{Guid.NewGuid():N}");

            try
            {
                await using (var decrypted = _crypto.DecryptToStream(encryptedPath, masterKey))
                await using (var zipOut = new FileStream(tempZipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    await decrypted.CopyToAsync(zipOut, cancellationToken);
                }

                Directory.CreateDirectory(tempExtractDir);
                ZipFile.ExtractToDirectory(tempZipPath, tempExtractDir, overwriteFiles: true);

                var dbBakPath = Path.Combine(tempExtractDir, "database.bak");
                if (!File.Exists(dbBakPath))
                    throw new InvalidDataException("Backup archive is missing database.bak.");

                var uploadsDir = Path.Combine(tempExtractDir, "uploads");
                if (!Directory.Exists(uploadsDir))
                    throw new InvalidDataException("Backup archive is missing uploads directory.");

                await RestoreDatabaseFromBakAsync(dbBakPath, cancellationToken);
                await RestoreUploadsAtomicAsync(uploadsDir, cancellationToken);

                await _audit.LogAsync("System Restore Completed", initiatedByUserId, details: Path.GetFileName(encryptedPath));
            }
            finally
            {
                TryDelete(tempZipPath);
                TryDeleteDirectory(tempExtractDir);
            }
        }

        private async Task CreatePlainBackupZipAsync(string outputZipPath, CancellationToken cancellationToken)
        {
            var sqlBackupDir = GetSqlServerBackupDirectory();
            Directory.CreateDirectory(sqlBackupDir);
            var tempDbBakPath = Path.Combine(sqlBackupDir, $"clinixdocs_db_{Guid.NewGuid():N}.bak");
            try
            {
                await BackupDatabaseToFileAsync(tempDbBakPath, cancellationToken);

                Directory.CreateDirectory(Path.GetDirectoryName(outputZipPath)!);
                using var zip = ZipFile.Open(outputZipPath, ZipArchiveMode.Create);
                zip.CreateEntryFromFile(tempDbBakPath, "database.bak", CompressionLevel.Optimal);

                var uploadsPhysical = Path.Combine(_env.WebRootPath, "uploads");
                if (Directory.Exists(uploadsPhysical))
                {
                    AddDirectoryToZip(zip, uploadsPhysical, "uploads");
                }
            }
            finally
            {
                TryDelete(tempDbBakPath);
            }
        }

        private async Task BackupDatabaseToFileAsync(string bakPath, CancellationToken cancellationToken)
        {
            var dbName = GetDatabaseNameFromConnectionString();
            var connStr = _config.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("DefaultConnection is missing.");

            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync(cancellationToken);

            var supportsCompression = await SupportsBackupCompressionAsync(conn, cancellationToken);
            var withOptions = supportsCompression
                ? "COPY_ONLY, COMPRESSION, INIT, CHECKSUM, STATS = 10"
                : "COPY_ONLY, INIT, CHECKSUM, STATS = 10";

            var cmdText = $@"
BACKUP DATABASE [{dbName}]
TO DISK = @p
WITH {withOptions};";

            await using var cmd = new SqlCommand(cmdText, conn)
            {
                CommandTimeout = 60 * 60 // 1 hour
            };
            cmd.Parameters.Add(new SqlParameter("@p", SqlDbType.NVarChar, 4000) { Value = bakPath });
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        private static async Task<bool> SupportsBackupCompressionAsync(SqlConnection conn, CancellationToken cancellationToken)
        {
            // EngineEdition: 4 = Express. Express does NOT support backup compression.
            // https://learn.microsoft.com/sql/t-sql/functions/serverproperty-transact-sql
            await using var cmd = new SqlCommand("SELECT CAST(SERVERPROPERTY('EngineEdition') AS int);", conn);
            var obj = await cmd.ExecuteScalarAsync(cancellationToken);
            var engineEdition = obj is int i ? i : Convert.ToInt32(obj ?? 0);
            return engineEdition != 4;
        }

        private string GetSqlServerBackupDirectory()
        {
            // Override if needed (must be a local path SQL Server service can write to)
            var configured = _config["BackupRecovery:SqlServerBackupDirectory"];
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }

            // Default to a shared, non-Program Files location so both:
            // - SQL Server service account can write the .bak
            // - Web app process can read it for encryption/packaging
            //
            // ProgramData is the recommended default for Windows services.
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (string.IsNullOrWhiteSpace(programData))
            {
                programData = @"C:\ProgramData";
            }

            return Path.Combine(programData, "ClinixDocs", "SqlBackups");
        }

        private async Task RestoreDatabaseFromBakAsync(string bakPath, CancellationToken cancellationToken)
        {
            var dbName = GetDatabaseNameFromConnectionString();
            var masterConnStr = BuildMasterConnectionString();

            await using var conn = new SqlConnection(masterConnStr);
            await conn.OpenAsync(cancellationToken);

            // Put database into single user to drop existing connections; then restore.
            var restoreSql = $@"
IF DB_ID(@db) IS NOT NULL
BEGIN
    ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
END

RESTORE DATABASE [{dbName}]
FROM DISK = @bak
WITH REPLACE, CHECKSUM, STATS = 10;

ALTER DATABASE [{dbName}] SET MULTI_USER;";

            await using var cmd = new SqlCommand(restoreSql, conn) { CommandTimeout = 60 * 60 };
            cmd.Parameters.Add(new SqlParameter("@db", SqlDbType.NVarChar, 128) { Value = dbName });
            cmd.Parameters.Add(new SqlParameter("@bak", SqlDbType.NVarChar, 4000) { Value = bakPath });
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        private Task RestoreUploadsAtomicAsync(string restoredUploadsDir, CancellationToken cancellationToken)
        {
            // Atomic-ish filesystem swap: rename current uploads -> backup, then move restored in place.
            var uploadsPhysical = Path.Combine(_env.WebRootPath, "uploads");
            var parent = Path.GetDirectoryName(uploadsPhysical)!;
            var rollbackDir = Path.Combine(parent, $"uploads_rollback_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}");

            Directory.CreateDirectory(parent);
            try
            {
                if (Directory.Exists(uploadsPhysical))
                {
                    Directory.Move(uploadsPhysical, rollbackDir);
                }

                CopyDirectory(restoredUploadsDir, uploadsPhysical);

                // Cleanup rollback on success
                TryDeleteDirectory(rollbackDir);
                return Task.CompletedTask;
            }
            catch
            {
                // Roll back
                TryDeleteDirectory(uploadsPhysical);
                if (Directory.Exists(rollbackDir))
                {
                    Directory.Move(rollbackDir, uploadsPhysical);
                }
                throw;
            }
        }

        private async Task<IAsyncDisposable> AcquireDbAppLockAsync(CancellationToken cancellationToken)
        {
            var connStr = _config.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("DefaultConnection is missing.");
            var conn = new SqlConnection(connStr);
            await conn.OpenAsync(cancellationToken);

            var cmd = new SqlCommand(@"
DECLARE @res int;
EXEC @res = sp_getapplock @Resource=@Resource, @LockMode=@LockMode, @LockOwner=@LockOwner, @LockTimeout=@LockTimeout;
SELECT @res;", conn)
            {
                CommandTimeout = 30
            };
            cmd.Parameters.AddWithValue("@Resource", "ClinixDocs.BackupRecovery");
            cmd.Parameters.AddWithValue("@LockMode", "Exclusive");
            cmd.Parameters.AddWithValue("@LockOwner", "Session");
            cmd.Parameters.AddWithValue("@LockTimeout", 0);

            var resultObj = await cmd.ExecuteScalarAsync(cancellationToken);
            var result = resultObj is int i ? i : Convert.ToInt32(resultObj ?? -1);
            if (result < 0)
            {
                await conn.DisposeAsync();
                throw new InvalidOperationException("Another backup/restore operation is already running.");
            }

            return new AppLockHandle(conn);
        }

        private sealed class AppLockHandle : IAsyncDisposable
        {
            private readonly SqlConnection _conn;
            public AppLockHandle(SqlConnection conn) => _conn = conn;

            public async ValueTask DisposeAsync()
            {
                try
                {
                    await using var cmd = new SqlCommand("EXEC sp_releaseapplock @Resource, @LockOwner;", _conn);
                    cmd.Parameters.AddWithValue("@Resource", "ClinixDocs.BackupRecovery");
                    cmd.Parameters.AddWithValue("@LockOwner", "Session");
                    await cmd.ExecuteNonQueryAsync();
                }
                catch
                {
                    // ignore
                }
                finally
                {
                    await _conn.DisposeAsync();
                }
            }
        }

        private byte[] GetEncryptionKey()
        {
            var base64 = _config["BackupRecovery:EncryptionKeyBase64"];
            if (string.IsNullOrWhiteSpace(base64))
                throw new InvalidOperationException("BackupRecovery:EncryptionKeyBase64 is not configured. Provide a 32-byte base64 key.");

            byte[] key;
            try { key = Convert.FromBase64String(base64); }
            catch { throw new InvalidOperationException("BackupRecovery:EncryptionKeyBase64 must be valid base64."); }

            if (key.Length != 32) throw new InvalidOperationException("BackupRecovery:EncryptionKeyBase64 must decode to exactly 32 bytes (AES-256 key).");
            return key;
        }

        private string GetLocalBackupsDirectory()
        {
            var configured = _config["BackupRecovery:LocalBackupDirectory"];
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }

            // Not under wwwroot (non-public)
            return Path.Combine(_env.ContentRootPath, "App_Data", "Backups");
        }

        private string GetDatabaseNameFromConnectionString()
        {
            var cs = _config.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("DefaultConnection is missing.");
            var builder = new SqlConnectionStringBuilder(cs);
            return builder.InitialCatalog;
        }

        private string BuildMasterConnectionString()
        {
            var cs = _config.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("DefaultConnection is missing.");
            var builder = new SqlConnectionStringBuilder(cs) { InitialCatalog = "master" };
            return builder.ToString();
        }

        private static void AddDirectoryToZip(ZipArchive zip, string sourceDir, string entryRoot)
        {
            foreach (var filePath in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(sourceDir, filePath);
                var entryName = Path.Combine(entryRoot, relative).Replace('\\', '/');
                zip.CreateEntryFromFile(filePath, entryName, CompressionLevel.Optimal);
            }
        }

        private static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (var dir in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(Path.Combine(destDir, Path.GetRelativePath(sourceDir, dir)));
            }
            foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var dest = Path.Combine(destDir, Path.GetRelativePath(sourceDir, file));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(file, dest, overwrite: true);
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static void TryDeleteDirectory(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
        }
    }
}

