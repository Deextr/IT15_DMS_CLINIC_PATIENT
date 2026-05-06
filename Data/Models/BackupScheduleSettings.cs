using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DMS_CPMS.Data.Models
{
    [Table("BackupScheduleSettings")]
    public class BackupScheduleSettings
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public int Id { get; set; } = 1; // singleton row

        [Required]
        public bool Enabled { get; set; }

        [Required]
        public BackupFrequency Frequency { get; set; } = BackupFrequency.Daily;

        /// <summary>
        /// For Weekly schedules only.
        /// </summary>
        public DayOfWeek? WeeklyDayOfWeek { get; set; }

        /// <summary>
        /// Local server time (HH:mm) when the scheduled backup should start.
        /// </summary>
        [Required]
        public TimeSpan StartTimeLocal { get; set; } = new TimeSpan(2, 0, 0); // 02:00

        public DateTime? LastRunUtc { get; set; }

        public DateTime? NextRunUtc { get; set; }
    }

    public enum BackupFrequency
    {
        Daily = 1,
        Weekly = 2
    }
}

