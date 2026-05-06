using System;
using System.Threading;
using System.Threading.Tasks;
using DMS_CPMS.Data;
using DMS_CPMS.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DMS_CPMS.Services.BackupRecovery
{
    public sealed class BackupSchedulerHostedService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<BackupSchedulerHostedService> _logger;

        public BackupSchedulerHostedService(IServiceProvider services, ILogger<BackupSchedulerHostedService> logger)
        {
            _services = services;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Small jitter to avoid thundering herd on restarts.
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await TickAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Backup scheduler tick failed.");
                }

                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }

        private async Task TickAsync(CancellationToken cancellationToken)
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var backup = scope.ServiceProvider.GetRequiredService<IBackupRecoveryService>();
            var audit = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

            var settings = await db.BackupScheduleSettings.FirstOrDefaultAsync(x => x.Id == 1, cancellationToken);
            if (settings == null || !settings.Enabled) return;

            var nowUtc = DateTime.UtcNow;
            var nextRunUtc = settings.NextRunUtc ?? ComputeNextRunUtc(settings, nowUtc);
            if (settings.NextRunUtc != nextRunUtc)
            {
                settings.NextRunUtc = nextRunUtc;
                await db.SaveChangesAsync(cancellationToken);
            }

            if (nextRunUtc.HasValue && nowUtc >= nextRunUtc.Value)
            {
                try
                {
                    await audit.LogAsync("Scheduled Backup Started", details: $"NextRunUtc={nextRunUtc:O}");
                    await backup.CreateBackupAsync(null, "System", cancellationToken);

                    settings.LastRunUtc = nowUtc;
                    settings.NextRunUtc = ComputeNextRunUtc(settings, nowUtc.AddSeconds(5));
                    await db.SaveChangesAsync(cancellationToken);
                    await audit.LogAsync("Scheduled Backup Completed");
                }
                catch (Exception ex)
                {
                    settings.NextRunUtc = ComputeNextRunUtc(settings, nowUtc.AddMinutes(1));
                    await db.SaveChangesAsync(cancellationToken);
                    await audit.LogAsync("Scheduled Backup Failed", details: ex.Message);
                    throw;
                }
            }
        }

        private static DateTime? ComputeNextRunUtc(BackupScheduleSettings s, DateTime nowUtc)
        {
            var localTz = TimeZoneInfo.Local;
            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, localTz);

            DateTime candidateLocal;
            var todayAt = nowLocal.Date.Add(s.StartTimeLocal);
            if (s.Frequency == BackupFrequency.Daily)
            {
                candidateLocal = todayAt > nowLocal ? todayAt : todayAt.AddDays(1);
            }
            else
            {
                var target = s.WeeklyDayOfWeek ?? DayOfWeek.Sunday;
                var daysAhead = ((int)target - (int)nowLocal.DayOfWeek + 7) % 7;
                candidateLocal = todayAt.AddDays(daysAhead);
                if (candidateLocal <= nowLocal) candidateLocal = candidateLocal.AddDays(7);
            }

            var candidateUtc = TimeZoneInfo.ConvertTimeToUtc(candidateLocal, localTz);
            return candidateUtc;
        }
    }
}

