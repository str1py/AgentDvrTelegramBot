using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Reflection.Metadata;
using CountryTelegramBot.Repositories;
using CountryTelegramBot.Services;

namespace CountryTelegramBot
{
    using CountryTelegramBot.Models;

    public class VideoWatcher : IVideoWatcher, IDisposable
    {
        /// <summary>
        /// Запуск наблюдения за папками
        /// </summary>
        public void StartWatching()
        {
            foreach (var watcher in watchers)
            {
                watcher.EnableRaisingEvents = true;
            }
            logger?.LogInformation("Наблюдение запущено.");
        }

        /// <summary>
        /// Остановка наблюдения за папками
        /// </summary>
        public void StopWatching()
        {
            foreach (var watcher in watchers)
            {
                watcher.EnableRaisingEvents = false;
            }
            logger?.LogInformation("Наблюдение остановлено.");
        }

        private enum WatcherType
        {
            ASAP,
            Morning,
            MorningAndEvening
        }
        private readonly List<string> folders;
        private readonly List<FileSystemWatcher> watchers;
        private readonly ITelegramBotService bot;
        private readonly ILogger<VideoWatcher>? logger;
        private IVideoRepository videoRepository;
        private WatcherType watcherType;
        private bool disposed;
        private readonly ITimeHelper timeHelper;
        private readonly IFileHelper fileHelper;
        private readonly IDailyScheduler dailyScheduler;
        private readonly IDbConnection dbConnection;



        public VideoWatcher(ITelegramBotService bot, IVideoRepository videoRepository, CountryTelegramBot.Configs.CommonConfig config, ITimeHelper timeHelper, IFileHelper fileHelper, IDbConnection dbConnection, IEnumerable<string>? watchFolders = null,
        ILogger<VideoWatcher>? logger = null)
        {
            this.bot = bot ?? throw new ArgumentNullException(nameof(bot));
            this.logger = logger;
            this.fileHelper = fileHelper ?? throw new ArgumentNullException(nameof(fileHelper));
            this.videoRepository = videoRepository ?? throw new ArgumentNullException(nameof(videoRepository));
            this.timeHelper = timeHelper ?? throw new ArgumentNullException(nameof(timeHelper));
            this.dbConnection = dbConnection ?? throw new ArgumentNullException(nameof(dbConnection));

            watcherType = GetWatcherType(config.WatcherType ?? "ASAP");

            folders = watchFolders?.ToList() ?? new List<string>();
            watchers = new List<FileSystemWatcher>();

            foreach (string folder in folders)
            {
                var watcher = fileHelper.CreateFolderWatcher(folder);
                if (watcher != null)
                {
                    watcher.Created += async (s, e) => await OnNewVideo(s, e);
                    watchers.Add(watcher);
                    logger?.LogInformation($"Мониторю {folder}...");
                }
            }
            StartWatching();
            dailyScheduler = new DailyScheduler(new TimerCallback[] { SendVideo, CheckReportsTimerCallback });
        }

        private async Task OnNewVideo(object sender, FileSystemEventArgs e)
        {
            try
            {
                logger?.LogInformation($"OnNewVideo вызван для файла: {e.FullPath}");
                
                // Проверяем, является ли файл видеофайлом
                var extension = Path.GetExtension(e.FullPath)?.ToLowerInvariant();
                logger?.LogInformation($"Расширение файла: {extension}");
                var videoExtensions = new[] { ".mp4", ".avi", ".mov", ".wmv", ".flv", ".webm" };
                
                if (!videoExtensions.Contains(extension))
                {
                    logger?.LogInformation($"Игнорируем файл с расширением {extension}: {e.FullPath}");
                    return;
                }
                
                await Task.Delay(30000); // Ждём 30 секунд, пока файл полностью запишется
                logger?.LogInformation($"{e.ChangeType}: {e.FullPath}");
                var path = e.FullPath;
                
                // Ищем превью-изображение в той же папке или в подпапке grabs
                var _path = Path.GetDirectoryName(e.FullPath);
                var folderPath = Path.Combine(_path, "grabs");
                logger?.LogInformation($"Путь к grabs скомбинирован {folderPath}");
                var grab = GetLastGrab(folderPath);
                
                if (grab == null)
                {
                    logger?.LogWarning($"Не найдено изображение для превью в {folderPath}");
                    // Продолжаем выполнение даже без превью
                }

                // Пытаемся добавить запись в базу данных
                try
                {
                    // If no grab image is found, use an empty string instead of null
                    await videoRepository.AddVideoAsync(path, grab ?? string.Empty);
                    logger?.LogInformation($"В базу данных добавлена новая запись ({Path.GetFileName(path)})");
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, $"Не удалось добавить запись в базу данных: {path}");
                }

                if (watcherType == WatcherType.ASAP && grab != null)
                {
                    try
                    {
                        var video = await videoRepository.GetLastVideoAsync();
                        if (video is not null)
                            await bot.SendVideoSafely(video.Path ?? "empty", video.Grab ?? "empty");
                    }
                    catch (Exception ex)
                    {
                        logger?.LogWarning(ex, "Не удалось отправить видео через Telegram");
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, $"Ошибка обработки нового видеофайла: {e.FullPath}");
            }

        }

        /// <summary>
        /// Проверяет, был ли отчет отправлен сегодня для указанного типа наблюдателя
        /// </summary>
        /// <returns>True, если отчет был отправлен сегодня, иначе false</returns>
        private async Task<bool> WasReportSentToday()
        {
            try
            {
                var now = DateTime.Now;
                DateTime startDate, endDate;
                
                // Определяем период времени в зависимости от типа наблюдателя
                if (watcherType == WatcherType.Morning)
                {
                    // Для утренних отчетов проверяем период прошлой ночи
                    startDate = timeHelper.NightVideoStartDate.AddDays(-1);
                    endDate = timeHelper.NightVideoEndDate.AddDays(-1);
                    logger?.LogInformation($"Проверка утреннего отчета за период: {startDate} - {endDate}");
                }
                else if (watcherType == WatcherType.MorningAndEvening)
                {
                    // Для утренне-вечерних отчетов проверяем, был ли отправлен отчет за дневной или ночной период
                    // Проверяем дневной период (с утра до вечера)
                    startDate = timeHelper.DayVideoStartDate;
                    endDate = timeHelper.DayVideoEndDate;
                    logger?.LogInformation($"Проверка дневного отчета за период: {startDate} - {endDate}");
                    
                    // Проверяем, был ли отправлен дневной отчет
                    var dayReportStatus = await dbConnection.GetReportStatusAsync(startDate, endDate);
                    if (dayReportStatus != null && dayReportStatus.IsSent && dayReportStatus.SentAt.HasValue && 
                        dayReportStatus.SentAt.Value.Date == now.Date)
                    {
                        logger?.LogInformation("Дневной отчет уже был отправлен сегодня");
                        return true;
                    }
                    
                    // Проверяем ночной период (с вечера до следующего утра)
                    startDate = timeHelper.NightVideoStartDate.AddDays(-1);
                    endDate = timeHelper.NightVideoEndDate.AddDays(-1);
                    logger?.LogInformation($"Проверка ночного отчета за период: {startDate} - {endDate}");
                }
                else
                {
                    // Для типа ASAP ежедневные отчеты не требуются
                    logger?.LogInformation("Для типа ASAP ежедневные отчеты не требуются");
                    return true;
                }
                
                // Проверяем, был ли отправлен отчет за определенный период
                var reportStatus = await dbConnection.GetReportStatusAsync(startDate, endDate);
                var wasSent = reportStatus != null && reportStatus.IsSent && reportStatus.SentAt.HasValue && 
                       reportStatus.SentAt.Value.Date == now.Date;
                
                if (wasSent)
                {
                    logger?.LogInformation("Отчет уже был отправлен сегодня");
                }
                else
                {
                    logger?.LogInformation("Отчет еще не был отправлен сегодня");
                }
                
                return wasSent;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Ошибка при проверке отправки отчета сегодня");
                return false; // Предполагаем, что отчет не отправлен, если произошла ошибка
            }
        }

        /// <summary>
        /// Отправляет отчет, если он не был отправлен сегодня, и добавляет запись в базу данных
        /// </summary>
        private async Task CheckAndSendReportIfNeeded()
        {
            try
            {
                // Проверяем только для типов наблюдателей Morning и MorningAndEvening
                if (watcherType != WatcherType.Morning && watcherType != WatcherType.MorningAndEvening)
                    return;

                var wasSent = await WasReportSentToday();
                if (!wasSent)
                {
                    logger?.LogInformation("Отчет за сегодня не был отправлен. Отправляю отчет...");
                    
                    // Определяем период времени в зависимости от типа наблюдателя
                    if (watcherType == WatcherType.Morning)
                    {
                        // Для утренних отчетов отправляем видео за прошлую ночь
                        var startDate = timeHelper.NightVideoStartDate.AddDays(-1);
                        var endDate = timeHelper.NightVideoEndDate.AddDays(-1);
                        logger?.LogInformation($"Отправка утреннего отчета за период: {startDate} - {endDate}");
                        var videos = await dbConnection.GetVideosAsync(startDate, endDate);
                        await bot.SendVideoGroupAsync(videos, startDate, endDate);
                    }
                    else if (watcherType == WatcherType.MorningAndEvening)
                    {
                        // Для утренне-вечерних отчетов отправляем видео за дневной и ночной периоды
                        // Отправляем дневные видео (с утра до вечера)
                        var dayStartDate = timeHelper.DayVideoStartDate;
                        var dayEndDate = timeHelper.DayVideoEndDate;
                        logger?.LogInformation($"Отправка дневного отчета за период: {dayStartDate} - {dayEndDate}");
                        var dayVideos = await dbConnection.GetVideosAsync(dayStartDate, dayEndDate);
                        await bot.SendVideoGroupAsync(dayVideos, dayStartDate, dayEndDate);
                        
                        // Отправляем ночные видео (с вечера до следующего утра)
                        var nightStartDate = timeHelper.NightVideoStartDate.AddDays(-1);
                        var nightEndDate = timeHelper.NightVideoEndDate.AddDays(-1);
                        logger?.LogInformation($"Отправка ночного отчета за период: {nightStartDate} - {nightEndDate}");
                        var nightVideos = await dbConnection.GetVideosAsync(nightStartDate, nightEndDate);
                        await bot.SendVideoGroupAsync(nightVideos, nightStartDate, nightEndDate);
                    }
                    
                    logger?.LogInformation("Отчет отправлен и запись в БД добавлена.");
                }
                else
                {
                    logger?.LogInformation("Отчет за сегодня уже был отправлен.");
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Ошибка при проверке и отправке отчета");
            }
        }

        private async void SendVideo(object? state)
        {
            try
            {
                var now = DateTime.Now;
                logger?.LogInformation($"{DateTime.Now.ToShortTimeString()}: Тик в SendVideo (WatcherType: {watcherType})");
                if (watcherType == WatcherType.Morning)
                {
                    if (now >= timeHelper.MorningReport)
                    {
                        logger?.LogInformation("Отправка утреннего отчета через SendVideo");
                        var vid = await videoRepository.GetVideosAsync(timeHelper.NightVideoStartDate, timeHelper.NightVideoEndDate);
                        await bot.SendVideoGroupAsync(vid, timeHelper.NightVideoStartDate, timeHelper.NightVideoEndDate);
                        timeHelper.CalculateNextNightPeriod();
                        timeHelper.CalculateNextMorningReport();
                    }
                }
                else if (watcherType == WatcherType.MorningAndEvening)
                {
                    if (now >= timeHelper.EveningReport)
                    {
                        logger?.LogInformation("Отправка утренне-вечернего отчета через SendVideo");
                        var vidNight = await videoRepository.GetVideosAsync(timeHelper.NightVideoStartDate, timeHelper.NightVideoEndDate);
                        var vidDay = await videoRepository.GetVideosAsync(timeHelper.DayVideoStartDate, timeHelper.DayVideoEndDate);

                        await bot.SendVideoGroupAsync(vidDay, timeHelper.DayVideoStartDate, timeHelper.DayVideoEndDate);
                        await bot.SendVideoGroupAsync(vidNight, timeHelper.NightVideoStartDate, timeHelper.NightVideoEndDate);

                        timeHelper.CalculateNextDayPeriod();
                        timeHelper.CalculateNextNightPeriod();
                        timeHelper.CalculateNextMorningReport();
                        timeHelper.CalculateNextEveningReport();
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Ошибка в методе SendVideo");
            }
        }
        
        private async void CheckReportsTimerCallback(object? state)
        {
            await CheckAndSendReportIfNeeded();
        }


        private string? GetLastGrab(string? folderPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                {
                    logger?.LogWarning($"Папка не существует: {folderPath}");
                    return null;
                }
                 
                var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };
                var imageFiles = Directory.GetFiles(folderPath)
                    .Where(file => imageExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                    .ToArray();
                if (imageFiles.Length == 0)
                {
                    logger?.LogWarning($"В папке нет изображений: {folderPath}");
                    return null;
                }
                var latestImage = imageFiles
                    .OrderByDescending(file => File.GetCreationTime(file))
                    .FirstOrDefault();
                return latestImage;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Ошибка при поиске изображений");
                return null;
            }
        }

        private WatcherType GetWatcherType(string type)
        {
            if (type == "ASAP")
            {
                logger?.LogInformation("Выбранный метод отчета: ASAP (отправка отчетов в реальном времени)");
                return WatcherType.ASAP;
            }
            else if (type == "Morning")
            {
                logger?.LogInformation("Выбранный метод отчета: Morning (отправка отчетов в утром)");
                return WatcherType.Morning;
            }
            else
            {
                logger?.LogInformation("Выбранный метод отчета: MorningAndEvening (отправка отчетов в утром и вечером)");
                return WatcherType.MorningAndEvening;
            }
        }
        public void Dispose()
        {
            //if (disposed) return;
            //foreach (var watcher in watchers)
            //{
            //    watcher.EnableRaisingEvents = false;
            //    watcher.Created -= async (s, e) => await OnNewVideo(s, e); ;
            //    watcher.Dispose();
            //}
            //disposed = true;
        }
    }
}