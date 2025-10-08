using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CountryTelegramBot.Models
{
    [Table("report_status")]
    public class ReportStatusModel
    {
        [Key]
        public int Id { get; set; }
        
        /// <summary>
        /// Start date of the report period
        /// </summary>
        public DateTime StartDate { get; set; }
        
        /// <summary>
        /// End date of the report period
        /// </summary>
        public DateTime EndDate { get; set; }
        
        /// <summary>
        /// Indicates whether the report was sent successfully
        /// </summary>
        public bool IsSent { get; set; }
        
        /// <summary>
        /// Timestamp when the report was attempted to be sent
        /// </summary>
        public DateTime AttemptedAt { get; set; }
        
        /// <summary>
        /// Timestamp when the report was successfully sent
        /// </summary>
        public DateTime? SentAt { get; set; }
        
        /// <summary>
        /// Error message if sending failed
        /// </summary>
        public string? ErrorMessage { get; set; }
    }
}