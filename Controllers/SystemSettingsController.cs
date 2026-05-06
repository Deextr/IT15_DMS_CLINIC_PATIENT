using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DMS_CPMS.Data;
using DMS_CPMS.Data.Models;
using DMS_CPMS.Models.SystemSettings;
using DMS_CPMS.Services;
using DMS_CPMS.Services.BackupRecovery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DMS_CPMS.Controllers
{
    [Authorize(Roles = "SuperAdmin,Admin")]
    public class SystemSettingsController : Controller
    {
        private const string ReauthPurpose = "BackupRecovery";
        private static readonly TimeSpan ReauthWindow = TimeSpan.FromMinutes(5);

        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IBackupRecoveryService _backupRecovery;
        private readonly IReauthService _reauth;
        private readonly IAuditLogService _audit;

        public SystemSettingsController(
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            IBackupRecoveryService backupRecovery,
            IReauthService reauth,
            IAuditLogService audit)
        {
            _db = db;
            _userManager = userManager;
            _backupRecovery = backupRecovery;
            _reauth = reauth;
            _audit = audit;
        }

        [HttpGet]
        public async Task<IActionResult> BackupRecovery(CancellationToken cancellationToken)
        {
            var schedule = await EnsureScheduleRowAsync(cancellationToken);
            var history = await _backupRecovery.GetHistoryAsync(50, cancellationToken);
            var userId = _userManager.GetUserId(User) ?? string.Empty;

            var vm = new BackupRecoveryViewModel
            {
                Schedule = schedule,
                History = history.ToList(),
                IsReauthenticated = _reauth.IsReauthenticated(userId, ReauthPurpose),
                ScheduleEnabled = schedule.Enabled,
                Frequency = schedule.Frequency,
                WeeklyDayOfWeek = schedule.WeeklyDayOfWeek,
                StartTimeLocal = $"{schedule.StartTimeLocal:hh\\:mm}"
            };

            return View("~/Views/SystemSettings/BackupRecovery.cshtml", vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmBackupRecoveryPassword(BackupRecoveryViewModel model, CancellationToken cancellationToken)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            if (string.IsNullOrWhiteSpace(model.ConfirmPassword))
            {
                TempData["Error"] = "Password is required.";
                return RedirectToAction(nameof(BackupRecovery));
            }

            var ok = await _userManager.CheckPasswordAsync(user, model.ConfirmPassword);
            await _audit.LogAsync("Backup/Recovery Reauth Attempt", user.Id, details: ok ? "Success" : "Failed");

            if (!ok)
            {
                TempData["Error"] = "Password confirmation failed.";
                return RedirectToAction(nameof(BackupRecovery));
            }

            _reauth.MarkReauthenticated(user.Id, ReauthPurpose, ReauthWindow);
            TempData["Success"] = "Password confirmed. You can now perform backup/restore actions for the next 5 minutes.";
            return RedirectToAction(nameof(BackupRecovery));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateBackup(CancellationToken cancellationToken)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            if (!_reauth.IsReauthenticated(user.Id, ReauthPurpose))
            {
                TempData["Error"] = "Please confirm your password before creating a backup.";
                return RedirectToAction(nameof(BackupRecovery));
            }

            try
            {
                var role = User.IsInRole("SuperAdmin") ? "SuperAdmin" : "Admin";
                await _backupRecovery.CreateBackupAsync(user.Id, role, cancellationToken);
                TempData["Success"] = "Backup created successfully.";
                return RedirectToAction(nameof(BackupRecovery));
            }
            catch (Exception ex)
            {
                TempData["Error"] = ToUserSafeMessage(ex);
                return RedirectToAction(nameof(BackupRecovery));
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreFromHistory(BackupRecoveryViewModel model, CancellationToken cancellationToken)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            if (!_reauth.IsReauthenticated(user.Id, ReauthPurpose))
            {
                TempData["Error"] = "Please confirm your password before restoring.";
                return RedirectToAction(nameof(BackupRecovery));
            }

            if (!ModelState.IsValid || model.RestoreBackupId == null)
            {
                TempData["Error"] = "You must select a backup and acknowledge the warning.";
                return RedirectToAction(nameof(BackupRecovery));
            }

            var role = User.IsInRole("SuperAdmin") ? "SuperAdmin" : "Admin";
            try
            {
                await _backupRecovery.RestoreFromExistingBackupAsync(model.RestoreBackupId.Value, user.Id, role, cancellationToken);
                TempData["Success"] = "System restored successfully.";
                return RedirectToAction(nameof(BackupRecovery));
            }
            catch (Exception ex)
            {
                TempData["Error"] = ToUserSafeMessage(ex);
                return RedirectToAction(nameof(BackupRecovery));
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(long.MaxValue)]
        public async Task<IActionResult> UploadAndRestore(IFormFile backupFile, bool restoreAcknowledge, CancellationToken cancellationToken)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            if (!_reauth.IsReauthenticated(user.Id, ReauthPurpose))
            {
                TempData["Error"] = "Please confirm your password before restoring.";
                return RedirectToAction(nameof(BackupRecovery));
            }

            if (!restoreAcknowledge)
            {
                TempData["Error"] = "You must acknowledge the destructive restore warning.";
                return RedirectToAction(nameof(BackupRecovery));
            }

            if (backupFile == null || backupFile.Length <= 0)
            {
                TempData["Error"] = "Please choose a backup file to upload.";
                return RedirectToAction(nameof(BackupRecovery));
            }

            if (!backupFile.FileName.EndsWith(".enc", StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "Only encrypted backup files (.enc) are allowed.";
                return RedirectToAction(nameof(BackupRecovery));
            }

            try
            {
                var role = User.IsInRole("SuperAdmin") ? "SuperAdmin" : "Admin";
                await using var stream = backupFile.OpenReadStream();
                await _backupRecovery.SaveUploadedBackupAndRestoreAsync(stream, backupFile.FileName, backupFile.Length, user.Id, role, cancellationToken);
                TempData["Success"] = "Uploaded backup restored successfully.";
                return RedirectToAction(nameof(BackupRecovery));
            }
            catch (Exception ex)
            {
                TempData["Error"] = ToUserSafeMessage(ex);
                return RedirectToAction(nameof(BackupRecovery));
            }
        }

        private static string ToUserSafeMessage(Exception ex)
        {
            // Keep messages helpful but not overly verbose/technical.
            if (ex is InvalidOperationException ioe &&
                ioe.Message.Contains("BackupRecovery:EncryptionKeyBase64", StringComparison.OrdinalIgnoreCase))
            {
                return "Backup encryption key is not configured. Set BackupRecovery:EncryptionKeyBase64 (32-byte base64 key) in User Secrets or your production secret store.";
            }

            return ex.Message.Length <= 300 ? ex.Message : ex.Message[..300];
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateBackupSchedule(BackupRecoveryViewModel model, CancellationToken cancellationToken)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            if (!_reauth.IsReauthenticated(user.Id, ReauthPurpose))
            {
                TempData["Error"] = "Please confirm your password before updating the schedule.";
                return RedirectToAction(nameof(BackupRecovery));
            }

            if (!TimeSpan.TryParseExact(model.StartTimeLocal, "hh\\:mm", CultureInfo.InvariantCulture, out var start))
            {
                TempData["Error"] = "Invalid start time.";
                return RedirectToAction(nameof(BackupRecovery));
            }

            var schedule = await EnsureScheduleRowAsync(cancellationToken);
            schedule.Enabled = model.ScheduleEnabled;
            schedule.Frequency = model.Frequency;
            schedule.WeeklyDayOfWeek = model.Frequency == BackupFrequency.Weekly ? model.WeeklyDayOfWeek : null;
            schedule.StartTimeLocal = start;
            schedule.NextRunUtc = null; // recalculated by scheduler

            await _db.SaveChangesAsync(cancellationToken);
            await _audit.LogAsync("Backup Schedule Updated", user.Id, details: $"{schedule.Enabled}/{schedule.Frequency}/{schedule.WeeklyDayOfWeek}/{schedule.StartTimeLocal:hh\\:mm}");

            TempData["Success"] = "Schedule updated.";
            return RedirectToAction(nameof(BackupRecovery));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DownloadBackup(int id, CancellationToken cancellationToken)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            if (!_reauth.IsReauthenticated(user.Id, ReauthPurpose))
            {
                TempData["Error"] = "Please confirm your password before downloading backups.";
                return RedirectToAction(nameof(BackupRecovery));
            }

            var backup = await _db.SystemBackups.FirstOrDefaultAsync(b => b.SystemBackupId == id, cancellationToken);
            if (backup == null) return NotFound();
            if (!string.Equals(backup.StorageProvider, "Local", StringComparison.OrdinalIgnoreCase)) return BadRequest();
            if (!System.IO.File.Exists(backup.StoragePath)) return NotFound();

            await _audit.LogAsync("System Backup Downloaded", user.Id, details: backup.FileName);
            var stream = new FileStream(backup.StoragePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return File(stream, "application/octet-stream", backup.FileName);
        }

        private async Task<BackupScheduleSettings> EnsureScheduleRowAsync(CancellationToken cancellationToken)
        {
            var schedule = await _db.BackupScheduleSettings.FirstOrDefaultAsync(x => x.Id == 1, cancellationToken);
            if (schedule != null) return schedule;

            schedule = new BackupScheduleSettings { Id = 1, Enabled = false };
            _db.BackupScheduleSettings.Add(schedule);
            await _db.SaveChangesAsync(cancellationToken);
            return schedule;
        }
    }
}

