using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
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
                return await CreateBackupCoreAsync(initiatedByUserId, initiatedByRole, cancellationToken);
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

        /// <summary>
        /// Performs backup file creation and DB registration. Caller must already hold <see cref="InProcessGate"/>
        /// and the DB session app lock (<see cref="AcquireDbAppLockAsync"/>).
        /// </summary>
        private async Task<SystemBackup> CreateBackupCoreAsync(string? initiatedByUserId, string? initiatedByRole, CancellationToken cancellationToken)
        {
            var nowUtc = DateTime.UtcNow;
            var stamp = nowUtc.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            var fileName = $"{BackupFilePrefix}{stamp}{_crypto.FileExtension}";
            var backupsDir = ResolveWritableLocalBackupDirectory();
            var outputPath = Path.Combine(backupsDir, fileName);

            var masterKey = GetEncryptionKey();

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

                // Pre-restore backup (must use core: outer scope already holds InProcessGate — CreateBackupAsync would deadlock)
                var pre = await CreateBackupCoreAsync(initiatedByUserId, initiatedByRole, cancellationToken);
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
                var backupsDir = ResolveWritableLocalBackupDirectory();
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

                // Pre-restore backup (must use core: outer scope already holds InProcessGate — CreateBackupAsync would deadlock)
                var pre = await CreateBackupCoreAsync(initiatedByUserId, initiatedByRole, cancellationToken);
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

                var dbExportPath = Path.Combine(tempExtractDir, "database.json");
                if (!File.Exists(dbExportPath))
                    throw new InvalidDataException("Backup archive is missing database.json.");

                var uploadsDir = Path.Combine(tempExtractDir, "uploads");
                if (!Directory.Exists(uploadsDir))
                    throw new InvalidDataException("Backup archive is missing uploads directory.");

                await RestoreDatabaseFromExportAsync(dbExportPath, cancellationToken);
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
            var tempDbExportPath = Path.Combine(Path.GetTempPath(), $"clinixdocs_dbexport_{Guid.NewGuid():N}.json");
            try
            {
                await ExportDatabaseToJsonAsync(tempDbExportPath, cancellationToken);

                Directory.CreateDirectory(Path.GetDirectoryName(outputZipPath)!);
                using var zip = ZipFile.Open(outputZipPath, ZipArchiveMode.Create);
                zip.CreateEntryFromFile(tempDbExportPath, "database.json", CompressionLevel.Optimal);

                var uploadsPhysical = Path.Combine(_env.WebRootPath, "uploads");
                if (Directory.Exists(uploadsPhysical))
                {
                    AddDirectoryToZip(zip, uploadsPhysical, "uploads");
                }
            }
            finally
            {
                TryDelete(tempDbExportPath);
            }
        }

        private async Task ExportDatabaseToJsonAsync(string outputJsonPath, CancellationToken cancellationToken)
        {
            var connStr = _config.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("DefaultConnection is missing.");
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync(cancellationToken);

            var export = new DatabaseExport
            {
                DatabaseName = GetDatabaseNameFromConnectionString(),
                ExportedUtc = DateTime.UtcNow
            };

            var tables = new List<(string Schema, string Name)>();
            const string tableSql = @"
SELECT s.name AS SchemaName, t.name AS TableName
FROM sys.tables t
INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE t.is_ms_shipped = 0
ORDER BY s.name, t.name;";

            await using (var tableCmd = new SqlCommand(tableSql, conn) { CommandTimeout = 120 })
            {
                await using var reader = await tableCmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    tables.Add((reader.GetString(0), reader.GetString(1)));
                }
            }

            foreach (var (schema, name) in tables)
            {
                var tableExport = new TableExport
                {
                    Schema = schema,
                    Name = name
                };

                var full = $"[{schema}].[{name}]";

                const string colSql = @"
SELECT c.name, ty.name AS SqlType, c.is_nullable, c.is_identity
FROM sys.columns c
INNER JOIN sys.types ty ON c.user_type_id = ty.user_type_id
INNER JOIN sys.tables t ON c.object_id = t.object_id
INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE s.name = @schema AND t.name = @table
ORDER BY c.column_id;";

                await using (var colCmd = new SqlCommand(colSql, conn) { CommandTimeout = 120 })
                {
                    colCmd.Parameters.AddWithValue("@schema", schema);
                    colCmd.Parameters.AddWithValue("@table", name);
                    await using var colReader = await colCmd.ExecuteReaderAsync(cancellationToken);
                    while (await colReader.ReadAsync(cancellationToken))
                    {
                        tableExport.Columns.Add(new ColumnExport
                        {
                            Name = colReader.GetString(0),
                            SqlType = colReader.GetString(1),
                            IsNullable = colReader.GetBoolean(2),
                            IsIdentity = colReader.GetBoolean(3)
                        });
                    }
                }

                await using (var dataCmd = new SqlCommand($"SELECT * FROM {full};", conn) { CommandTimeout = 60 * 60 })
                await using (var dataReader = await dataCmd.ExecuteReaderAsync(cancellationToken))
                {
                    while (await dataReader.ReadAsync(cancellationToken))
                    {
                        var row = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                        for (var i = 0; i < dataReader.FieldCount; i++)
                        {
                            var val = dataReader.IsDBNull(i) ? null : dataReader.GetValue(i);
                            row[dataReader.GetName(i)] = JsonSerializer.SerializeToElement(val, val?.GetType() ?? typeof(object));
                        }
                        tableExport.Rows.Add(row);
                    }
                }

                export.Tables.Add(tableExport);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputJsonPath)!);
            await using var fs = new FileStream(outputJsonPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await JsonSerializer.SerializeAsync(fs, export, cancellationToken: cancellationToken);
        }

        private async Task RestoreDatabaseFromExportAsync(string exportJsonPath, CancellationToken cancellationToken)
        {
            await using var fs = new FileStream(exportJsonPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var payload = await JsonSerializer.DeserializeAsync<DatabaseExport>(fs, cancellationToken: cancellationToken)
                ?? throw new InvalidDataException("Invalid database export payload.");

            var connStr = _config.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("DefaultConnection is missing.");
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync(cancellationToken);
            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(cancellationToken);

            try
            {
                await ExecuteNonQueryAsync(conn, tx, "EXEC sp_msforeachtable 'ALTER TABLE ? NOCHECK CONSTRAINT ALL';", cancellationToken);
                await ExecuteNonQueryAsync(conn, tx, "EXEC sp_msforeachtable 'DISABLE TRIGGER ALL ON ?';", cancellationToken);

                foreach (var table in payload.Tables)
                {
                    var tableName = $"[{table.Schema}].[{table.Name}]";
                    await ExecuteNonQueryAsync(conn, tx, $"DELETE FROM {tableName};", cancellationToken);
                }

                foreach (var table in payload.Tables)
                {
                    if (table.Rows.Count == 0) continue;

                    var tableName = $"[{table.Schema}].[{table.Name}]";
                    var columnList = table.Columns.Select(c => $"[{c.Name}]").ToList();
                    var hasIdentity = table.Columns.Any(c => c.IsIdentity);

                    if (hasIdentity)
                    {
                        await ExecuteNonQueryAsync(conn, tx, $"SET IDENTITY_INSERT {tableName} ON;", cancellationToken);
                    }

                    try
                    {
                        var insertSql = $"INSERT INTO {tableName} ({string.Join(", ", columnList)}) VALUES ({string.Join(", ", table.Columns.Select((_, i) => $"@p{i}"))});";
                        foreach (var row in table.Rows)
                        {
                            await using var cmd = new SqlCommand(insertSql, conn, tx)
                            {
                                CommandTimeout = 120
                            };

                            for (var i = 0; i < table.Columns.Count; i++)
                            {
                                var col = table.Columns[i];
                                row.TryGetValue(col.Name, out var elem);
                                var value = ConvertJsonToSqlValue(elem, col.SqlType);
                                AddTypedParameter(cmd, $"@p{i}", col.SqlType, value);
                            }

                            await cmd.ExecuteNonQueryAsync(cancellationToken);
                        }
                    }
                    finally
                    {
                        if (hasIdentity)
                        {
                            await ExecuteNonQueryAsync(conn, tx, $"SET IDENTITY_INSERT {tableName} OFF;", cancellationToken);
                        }
                    }
                }

                await ExecuteNonQueryAsync(conn, tx, "EXEC sp_msforeachtable 'ALTER TABLE ? WITH CHECK CHECK CONSTRAINT ALL';", cancellationToken);
                await ExecuteNonQueryAsync(conn, tx, "EXEC sp_msforeachtable 'ENABLE TRIGGER ALL ON ?';", cancellationToken);
                await tx.CommitAsync(cancellationToken);
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken);
                throw;
            }
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

        /// <summary>
        /// Resolves a folder the app can actually write to. Shared hosts (e.g. MonsterASP) often deny arbitrary paths like D:\home\site\backups;
        /// the app falls back to <c>ContentRoot/App_Data/Backups</c> automatically.
        /// </summary>
        private string ResolveWritableLocalBackupDirectory()
        {
            var configuredRaw = _config["BackupRecovery:LocalBackupDirectory"]?.Trim();
            string? configuredFull = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(configuredRaw))
                    configuredFull = Path.GetFullPath(configuredRaw);
            }
            catch
            {
                configuredFull = null;
            }

            var appDataBackupsFull = Path.GetFullPath(Path.Combine(_env.ContentRootPath, "App_Data", "Backups"));

            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(configuredFull))
                candidates.Add(configuredFull);
            if (!candidates.Contains(appDataBackupsFull, StringComparer.OrdinalIgnoreCase))
                candidates.Add(appDataBackupsFull);

            foreach (var path in candidates)
            {
                if (!TryPrepareWritableBackupDirectory(path, out _))
                    continue;

                if (!string.IsNullOrEmpty(configuredFull) &&
                    !path.Equals(configuredFull, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(
                        "BackupRecovery LocalBackupDirectory is not writable ({Configured}). Using fallback under the site App_Data folder: {Fallback}. Tip: delete env var BackupRecovery__LocalBackupDirectory or set it to that path.",
                        configuredFull, path);
                }
                else
                {
                    _logger.LogInformation("Using backup directory: {Path}", path);
                }

                return path;
            }

            throw new InvalidOperationException(
                "Could not prepare a writable backup directory. Remove BackupRecovery__LocalBackupDirectory so the app uses App_Data/Backups, " +
                "or ask your host to grant the app identity write permission. " +
                $"Tried: {string.Join(", ", candidates.Select(p => '\"' + p + '\"'))}.");
        }

        private static bool TryPrepareWritableBackupDirectory(string directoryPath, out string? error)
        {
            error = null;
            try
            {
                Directory.CreateDirectory(directoryPath);
                var probe = Path.Combine(directoryPath, $".acl_probe_{Guid.NewGuid():N}.tmp");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch (UnauthorizedAccessException ex)
            {
                error = ex.Message;
                return false;
            }
            catch (IOException ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private string GetDatabaseNameFromConnectionString()
        {
            var cs = _config.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("DefaultConnection is missing.");
            var builder = new SqlConnectionStringBuilder(cs);
            return builder.InitialCatalog;
        }

        private static async Task ExecuteNonQueryAsync(SqlConnection conn, SqlTransaction tx, string sql, CancellationToken cancellationToken)
        {
            await using var cmd = new SqlCommand(sql, conn, tx) { CommandTimeout = 60 * 60 };
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        private static void AddTypedParameter(SqlCommand cmd, string parameterName, string sqlType, object? value)
        {
            var t = sqlType.ToLowerInvariant();
            var parameter = t switch
            {
                "bit" => cmd.Parameters.Add(parameterName, SqlDbType.Bit),
                "tinyint" => cmd.Parameters.Add(parameterName, SqlDbType.TinyInt),
                "smallint" => cmd.Parameters.Add(parameterName, SqlDbType.SmallInt),
                "int" => cmd.Parameters.Add(parameterName, SqlDbType.Int),
                "bigint" => cmd.Parameters.Add(parameterName, SqlDbType.BigInt),
                "real" => cmd.Parameters.Add(parameterName, SqlDbType.Real),
                "float" => cmd.Parameters.Add(parameterName, SqlDbType.Float),
                "decimal" or "numeric" => cmd.Parameters.Add(parameterName, SqlDbType.Decimal),
                "money" => cmd.Parameters.Add(parameterName, SqlDbType.Money),
                "smallmoney" => cmd.Parameters.Add(parameterName, SqlDbType.SmallMoney),
                "uniqueidentifier" => cmd.Parameters.Add(parameterName, SqlDbType.UniqueIdentifier),
                "date" => cmd.Parameters.Add(parameterName, SqlDbType.Date),
                "datetime" => cmd.Parameters.Add(parameterName, SqlDbType.DateTime),
                "datetime2" => cmd.Parameters.Add(parameterName, SqlDbType.DateTime2),
                "smalldatetime" => cmd.Parameters.Add(parameterName, SqlDbType.SmallDateTime),
                "datetimeoffset" => cmd.Parameters.Add(parameterName, SqlDbType.DateTimeOffset),
                "time" => cmd.Parameters.Add(parameterName, SqlDbType.Time),
                "binary" or "varbinary" or "image" or "rowversion" or "timestamp" => cmd.Parameters.Add(parameterName, SqlDbType.VarBinary),
                _ => cmd.Parameters.Add(parameterName, SqlDbType.NVarChar)
            };

            if (value == null)
            {
                parameter.Value = DBNull.Value;
                return;
            }

            if (t == "datetime" && value is DateTime dt && dt < new DateTime(1753, 1, 1))
                value = new DateTime(1753, 1, 1);
            if (t == "smalldatetime" && value is DateTime sdt && sdt < new DateTime(1900, 1, 1))
                value = new DateTime(1900, 1, 1);

            parameter.Value = value;
        }

        private static object? ConvertJsonToSqlValue(JsonElement value, string sqlType)
        {
            if (value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.Undefined)
                return null;

            var t = sqlType.ToLowerInvariant();
            return t switch
            {
                "bit" => value.GetBoolean(),
                "tinyint" => value.GetByte(),
                "smallint" => value.GetInt16(),
                "int" => value.GetInt32(),
                "bigint" => value.GetInt64(),
                "real" => value.GetSingle(),
                "float" => value.GetDouble(),
                "decimal" or "numeric" or "money" or "smallmoney" => value.GetDecimal(),
                "uniqueidentifier" => value.GetGuid(),
                "date" or "datetime" or "datetime2" or "smalldatetime" => value.GetDateTime(),
                "datetimeoffset" => value.GetDateTimeOffset(),
                "time" => TimeSpan.Parse(value.GetString() ?? "00:00:00", CultureInfo.InvariantCulture),
                "binary" or "varbinary" or "image" or "rowversion" or "timestamp" => Convert.FromBase64String(value.GetString() ?? string.Empty),
                _ => value.GetString()
            };
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

    public sealed class DatabaseExport
    {
        public string DatabaseName { get; set; } = string.Empty;
        public DateTime ExportedUtc { get; set; }
        public List<TableExport> Tables { get; set; } = new();
    }

    public sealed class TableExport
    {
        public string Schema { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public List<ColumnExport> Columns { get; set; } = new();
        public List<Dictionary<string, JsonElement>> Rows { get; set; } = new();
    }

    public sealed class ColumnExport
    {
        public string Name { get; set; } = string.Empty;
        public string SqlType { get; set; } = string.Empty;
        public bool IsNullable { get; set; }
        public bool IsIdentity { get; set; }
    }
}

