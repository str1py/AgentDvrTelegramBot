using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;

namespace CountryTelegramBot
{
    using CountryTelegramBot.Models;

    public class DbConnection : IDbConnection
    {
    
        public bool IsConnected { get; private set; } = false;
        private readonly DbCountryContext dbCountryContext;
        private readonly ILogger? logger;
        private readonly IErrorHandler? errorHandler;

        public DbConnection(ILogger? logger, DbCountryContext dbCountryContext, IErrorHandler? errorHandler = null)
        {
            this.logger = logger;
            this.errorHandler = errorHandler;
            this.dbCountryContext = dbCountryContext;
            
            try
            {
                IsConnected = dbCountryContext.Database.CanConnect();
                if (IsConnected)
                    logger?.LogInformation($"Подключение к базе данных установлено успешно!");
                else
                    logger?.LogError("Ошибка подключения");
                // Автоматическое удаление битых записей при запуске
                if (IsConnected)
                    RemoveBrokenVideos();
            }
            catch (Exception ex)
            {
                logger?.LogError($"Ошибка подключения: {ex.Message}");
                errorHandler?.HandleError(ex, "Ошибка подключения к базе данных");
            }
        }
        /// <summary>
        /// Проверяет целостность данных: возвращает список записей, для которых отсутствует файл по указанному пути.
        /// </summary>
        public List<VideoModel> GetBrokenVideos()
        {
            var broken = new List<VideoModel>();
            var allVideos = dbCountryContext.Video.AsNoTracking().ToList();
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
                dbCountryContext.Video.RemoveRange(broken);
                dbCountryContext.SaveChanges();
                logger?.LogWarning($"Удалено {broken.Count} битых записей из базы данных.");
            }
        }

        public async Task AddVideoData(string path, string grab)
        {
            dbCountryContext.Video
                .Add(new VideoModel { Path = path, Grab = grab, Date = DateTime.Now });
            await dbCountryContext.SaveChangesAsync();
        }

        public List<VideoModel> GetVideos(DateTime startDate, DateTime endDate)
        {
            return dbCountryContext.Video
                .AsNoTracking()
                .Where(v => v.Date >= startDate && v.Date <= endDate)
                .ToList();
        }
        
        public async Task<List<VideoModel>> GetVideosAsync(DateTime startDate, DateTime endDate)
        {
            return await dbCountryContext.Video
                .AsNoTracking()
                .Where(v => v.Date >= startDate && v.Date <= endDate)
                .ToListAsync();
        }
       
        public async Task<VideoModel> GetLastVideo()
        {
            var result = await dbCountryContext.Video.AsNoTracking().OrderByDescending(v => v.Date).FirstOrDefaultAsync();
            return result ?? new VideoModel();
        }

        public async Task<bool> RemoveItemByPath(string path)
        {
            var item = await dbCountryContext.Video.FirstOrDefaultAsync(x => x.Path == path);
            if (item != null)
            {
                dbCountryContext.Remove(item);
                await dbCountryContext.SaveChangesAsync();
                return true;
            }
            return false;
    
        }
   
        // Методы для работы со статусом отчетов
        
        /// <summary>
        /// Добавляет новую запись о статусе отчета
        /// </summary>
        public async Task AddReportStatus(DateTime startDate, DateTime endDate, bool isSent, string? errorMessage = null)
        {
            logger?.LogInformation($"Добавление статуса отчета в БД: {startDate} - {endDate}, Отправлено: {isSent}, Ошибка: {errorMessage}");
            
            var reportStatus = new ReportStatusModel
            {
                StartDate = startDate,
                EndDate = endDate,
                IsSent = isSent,
                AttemptedAt = DateTime.Now,
                SentAt = isSent ? DateTime.Now : (DateTime?)null,
                ErrorMessage = errorMessage
            };
            
            dbCountryContext.ReportStatus.Add(reportStatus);
            await dbCountryContext.SaveChangesAsync();
            
            logger?.LogInformation($"Статус отчета успешно добавлен в БД с ID: {reportStatus.Id}");
        }
        
        /// <summary>
        /// Обновляет существующую запись о статусе отчета
        /// </summary>
        public async Task UpdateReportStatus(int id, bool isSent, string? errorMessage = null)
        {
            logger?.LogInformation($"Обновление статуса отчета в БД (ID: {id}): Отправлено: {isSent}, Ошибка: {errorMessage}");
            
            var reportStatus = await dbCountryContext.ReportStatus.FindAsync(id);
            if (reportStatus != null)
            {
                reportStatus.IsSent = isSent;
                reportStatus.AttemptedAt = DateTime.Now;
                if (isSent)
                {
                    reportStatus.SentAt = DateTime.Now;
                    logger?.LogInformation($"Установка времени отправки: {reportStatus.SentAt}");
                }
                reportStatus.ErrorMessage = errorMessage;
                
                dbCountryContext.ReportStatus.Update(reportStatus);
                await dbCountryContext.SaveChangesAsync();
                
                logger?.LogInformation($"Статус отчета успешно обновлен в БД (ID: {id})");
            }
            else
            {
                logger?.LogWarning($"Не удалось найти запись о статусе отчета в БД (ID: {id})");
            }
        }
        
        /// <summary>
        /// Получает все неотправленные отчеты
        /// </summary>
        public List<ReportStatusModel> GetUnsentReports()
        {
            logger?.LogInformation("Получение всех неотправленных отчетов из БД");
            var unsentReports = dbCountryContext.ReportStatus
                .AsNoTracking()
                .Where(r => !r.IsSent)
                .ToList();
            logger?.LogInformation($"Найдено {unsentReports.Count} неотправленных отчетов");
            
            // Логируем информацию о каждом неотправленном отчете
            foreach (var report in unsentReports)
            {
                logger?.LogInformation($"Неотправленный отчет (ID: {report.Id}): {report.StartDate} - {report.EndDate}");
            }
            
            return unsentReports;
        }
        
        /// <summary>
        /// Получает статус отчета по диапазону дат
        /// </summary>
        public ReportStatusModel? GetReportStatus(DateTime startDate, DateTime endDate)
        {
            logger?.LogInformation($"Поиск статуса отчета в БД: {startDate} - {endDate}");
            var reportStatus = dbCountryContext.ReportStatus
                .AsNoTracking()
                .FirstOrDefault(r => r.StartDate == startDate && r.EndDate == endDate);
            
            if (reportStatus != null)
            {
                logger?.LogInformation($"Найден статус отчета в БД (ID: {reportStatus.Id}): Отправлено: {reportStatus.IsSent}, Дата отправки: {reportStatus.SentAt}");
            }
            else
            {
                logger?.LogInformation("Статус отчета не найден в БД");
            }
            
            return reportStatus;
        }
        
        /// <summary>
        /// Асинхронно получает статус отчета по диапазону дат
        /// </summary>
        public async Task<ReportStatusModel?> GetReportStatusAsync(DateTime startDate, DateTime endDate)
        {
            logger?.LogInformation($"Асинхронный поиск статуса отчета в БД: {startDate} - {endDate}");
            var reportStatus = await dbCountryContext.ReportStatus
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.StartDate == startDate && r.EndDate == endDate);
            
            if (reportStatus != null)
            {
                logger?.LogInformation($"Найден статус отчета в БД (ID: {reportStatus.Id}): Отправлено: {reportStatus.IsSent}, Дата отправки: {reportStatus.SentAt}");
            }
            else
            {
                logger?.LogInformation("Статус отчета не найден в БД");
            }
            
            return reportStatus;
        }
    }
}