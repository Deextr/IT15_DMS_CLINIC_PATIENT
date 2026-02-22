using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DMS_CPMS.Data.Models
{
    [Table("RetentionPolicy")]
    public class RetentionPolicy
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int RetentionPolicyID { get; set; }

        [Required]
        [StringLength(100)]
        public string ModuleName { get; set; } = string.Empty;

        /// <summary>
        /// Retention duration in months (e.g. 60 = 5 years, 12 = 1 year).
        /// </summary>
        [Required]
        public int RetentionDurationMonths { get; set; }

        /// <summary>
        /// Action to take after retention expires: "NotifyAdmin", "AutoDelete", or "ManualReview".
        /// </summary>
        [Required]
        [StringLength(50)]
        public string AutoActionAfterExpiry { get; set; } = "ManualReview";

        /// <summary>
        /// Whether this retention policy is currently active.
        /// </summary>
        [Required]
        public bool IsEnabled { get; set; } = true;
    }
}
