using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using DMS_CPMS.Data.Models;

namespace DMS_CPMS.Models.SystemSettings
{
    public class BackupRecoveryViewModel
    {
        public List<SystemBackup> History { get; set; } = new();
        public BackupScheduleSettings Schedule { get; set; } = new();

        public bool IsReauthenticated { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        public string? ConfirmPassword { get; set; }

        [Display(Name = "Enable scheduled backups")]
        public bool ScheduleEnabled { get; set; }

        [Display(Name = "Frequency")]
        public BackupFrequency Frequency { get; set; } = BackupFrequency.Daily;

        [Display(Name = "Weekly day")]
        public DayOfWeek? WeeklyDayOfWeek { get; set; }

        [Display(Name = "Start time (local)")]
        [RegularExpression(@"^\d{2}:\d{2}$", ErrorMessage = "Time must be in HH:mm format.")]
        public string StartTimeLocal { get; set; } = "02:00";

        public int? RestoreBackupId { get; set; }

        [Display(Name = "I understand this will overwrite current system data")]
        [Range(typeof(bool), "true", "true", ErrorMessage = "You must acknowledge the destructive restore warning.")]
        public bool RestoreAcknowledge { get; set; }
    }
}

