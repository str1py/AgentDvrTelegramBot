using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;

namespace CountryTelegramBot
{
    using CountryTelegramBot.Models;

    public class DbConnection : DbContext, IDbConnection
    {
    
        public bool IsConnected { get; private set; } = false;
        public DbCountryContext DbCountryContext { get; private set; }
        private readonly ILogger? logger;
        private readonly IErrorHandler? errorHandler;

        public DbConnection(ILogger? logger, DbContextOptions<DbCountryContext> options, IErrorHandler? errorHandler = null)
        {
            this.logger = logger;
            this.errorHandler = errorHandler;
            try
            {
                DbCountryContext = new DbCountryContext(options);
                IsConnected = DbCountryContext.Database.CanConnect();
                if (IsConnected)
                    logger?.LogInformation($"Connected to database successfully! Connection string: {MaskSecret(options?.ToString())}");
                else
                    logger?.LogError($"Connection error");
                // Автоматическое удаление битых записей при запуске
                if (IsConnected)
                    RemoveBrokenVideos();
            }
            catch (Exception ex)
            {
                logger?.LogError($"Connection error: {ex.Message}");
                errorHandler?.HandleError(ex, "Ошибка подключения к базе данных");
            }
        }
        /// <summary>
        /// Проверяет целостность данных: возвращает список записей, для которых отсутствует файл по указанному пути.
        /// </summary>
        public List<VideoModel> GetBrokenVideos()
        {
            var broken = new List<VideoModel>();
            var allVideos = DbCountryContext.Video.AsNoTracking().ToList();
            foreach (var video in allVideos)
            {
                if (!System.IO.File.Exists(video.Path))
                {
                    broken.Add(video);
                }
            }
            return broken;
        }
    
        /// <summary>
        /// Удаляет битые записи (файлы, которых нет на диске) из базы данных
        /// </summary>
        private void RemoveBrokenVideos()
        {
            var broken = GetBrokenVideos();
            if (broken.Count > 0)
            {
                DbCountryContext.Video.RemoveRange(broken);
                DbCountryContext.SaveChanges();
                logger?.LogWarning($"Удалено {broken.Count} битых записей из базы данных.");
            }
        }

        private string MaskSecret(string? secret)
        {
            if (string.IsNullOrEmpty(secret)) return "[empty]";
            if (secret.Length <= 8) return "********";
            return secret.Substring(0, 4) + new string('*', secret.Length - 8) + secret.Substring(secret.Length - 4);
        }
        

        public async Task AddVideoData(string path, string grab)
        {
            DbCountryContext.Video
                .Add(new VideoModel { Path = path, Grab = grab, Date = DateTime.Now });
            await DbCountryContext.SaveChangesAsync();
        }

        public List<VideoModel> GetVideos(DateTime startDate, DateTime endDate)
        {
            return DbCountryContext.Video
                .AsNoTracking()
                .Where(v => v.Date >= startDate && v.Date <= endDate)
                .ToList();
        }
       
        public async Task<VideoModel> GetLastVideo()
        {
            var result = await DbCountryContext.Video.AsNoTracking().OrderByDescending(v => v.Date).FirstOrDefaultAsync();
            return result ?? new VideoModel();
        }

        public async Task<bool> RemoveItemByPath(string path)
        {
            var item = await DbCountryContext.Video.FirstOrDefaultAsync(x => x.Path == path);
            if (item != null)
            {
                DbCountryContext.Remove(item);
                await DbCountryContext.SaveChangesAsync();
                return true;
            }
            return false;
    
        }
   
        // Report Status Methods
        
        /// <summary>
        /// Adds a new report status record
        /// </summary>
        public async Task AddReportStatus(DateTime startDate, DateTime endDate, bool isSent, string? errorMessage = null)
        {
            var reportStatus = new ReportStatusModel
            {
                StartDate = startDate,
                EndDate = endDate,
                IsSent = isSent,
                AttemptedAt = DateTime.Now,
                SentAt = isSent ? DateTime.Now : (DateTime?)null,
                ErrorMessage = errorMessage
            };
            
            DbCountryContext.ReportStatus.Add(reportStatus);
            await DbCountryContext.SaveChangesAsync();
        }
        
        /// <summary>
        /// Updates an existing report status record
        /// </summary>
        public async Task UpdateReportStatus(int id, bool isSent, string? errorMessage = null)
        {
            var reportStatus = await DbCountryContext.ReportStatus.FindAsync(id);
            if (reportStatus != null)
            {
                reportStatus.IsSent = isSent;
                reportStatus.AttemptedAt = DateTime.Now;
                if (isSent)
                    reportStatus.SentAt = DateTime.Now;
                reportStatus.ErrorMessage = errorMessage;
                
                await DbCountryContext.SaveChangesAsync();
            }
        }
        
        /// <summary>
        /// Gets all unsent reports
        /// </summary>
        public List<ReportStatusModel> GetUnsentReports()
        {
            return DbCountryContext.ReportStatus
                .AsNoTracking()
                .Where(r => !r.IsSent)
                .ToList();
        }
        
        /// <summary>
        /// Gets report status by date range
        /// </summary>
        public ReportStatusModel? GetReportStatus(DateTime startDate, DateTime endDate)
        {
            return DbCountryContext.ReportStatus
                .AsNoTracking()
                .FirstOrDefault(r => r.StartDate == startDate && r.EndDate == endDate);
        }
    }
}