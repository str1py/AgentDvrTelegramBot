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

        private readonly List<string> folders;
        private readonly List<FileSystemWatcher> watchers;
        private readonly ITelegramBotService bot;
        private readonly ILogger<VideoWatcher>? logger;
        private IVideoRepository videoRepository;
        private CountryTelegramBot.Services.WatcherType watcherType;
        private bool disposed;
        private readonly ITimeHelper timeHelper;
        private readonly IFileHelper fileHelper;
        private readonly IDailyScheduler dailyScheduler;
        private readonly IDbConnection dbConnection;

        public VideoWatcher(
            ITelegramBotService bot, 
            IVideoRepository videoRepository, 
            CountryTelegramBot.Configs.CommonConfig config, 
            ITimeHelper timeHelper, 
            IFileHelper fileHelper, 
            IDbConnection dbConnection, 
            IEnumerable<string>? watchFolders = null,
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
            dailyScheduler = new DailyScheduler(SendVideo);
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
                
                // Проверяем, не является ли файл сжатым видео (содержит "_compressed" в имени)
                if (Path.GetFileName(e.FullPath).Contains("_compressed"))
                {
                    logger?.LogInformation($"Пропущено сжатое видео (содержит '_compressed' в имени): {e.FullPath}");
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

                if (watcherType == CountryTelegramBot.Services.WatcherType.ASAP && grab != null)
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

        private async void SendVideo(object? state)
        {
            try
            {
                var now = DateTime.Now;
                logger.LogInformation($"{DateTime.Now.ToShortTimeString()}: Планированная отправка отчета (WatcherType: {watcherType})");
                
                // Проверяем, нужно ли отправлять отчеты в зависимости от типа наблюдателя
                bool shouldSendReport = false;
                
                if (watcherType == CountryTelegramBot.Services.WatcherType.Morning)
                {
                    if (now >= timeHelper.MorningReport)
                    {
                        shouldSendReport = true;
                        logger.LogInformation("Отправка утреннего отчета через SendVideo");
                    }
                }
                else if (watcherType == CountryTelegramBot.Services.WatcherType.MorningAndEvening)
                {
                    if (now >= timeHelper.EveningReport)
                    {
                        shouldSendReport = true;
                        logger.LogInformation("Отправка утренне-вечернего отчета через SendVideo");
                    }
                }
                
                if (shouldSendReport)
                {
                    // Проверяем, не был ли отчет уже отправлен
                    if (watcherType == CountryTelegramBot.Services.WatcherType.Morning)
                    {
                        // Для утреннего типа проверяем отчет за предыдущую ночь
                        // Сегодня 10.10.2025, проверяем период с 09.10.2025 23:00 до 10.10.2025 08:00
                        var startDate = timeHelper.NightVideoStartDate.AddDays(-1);
                        var endDate = timeHelper.NightVideoEndDate.AddDays(-1);
                        
                        // Проверяем статус отчета перед отправкой (асинхронно)
                        var reportStatus = await dbConnection.GetReportStatusAsync(startDate, endDate);
                        if (reportStatus != null && reportStatus.IsSent)
                        {
                            logger.LogInformation($"Утренний отчет за {startDate} - {endDate} уже был отправлен ранее ({reportStatus.SentAt})");
                            return;
                        }
                        
                        var vid = await videoRepository.GetVideosAsync(startDate, endDate);
                        await bot.SendVideoGroupAsync(vid, startDate, endDate);
                        // Убираем вызовы обновления дат, так как они теперь вычисляются динамически
                    }
                    else if (watcherType == CountryTelegramBot.Services.WatcherType.MorningAndEvening)
                    {
                        // Для утренне-вечернего типа проверяем оба периода
                        // Ночной отчет: с вечера вчерашнего дня до утра сегодня
                        var nightStartDate = timeHelper.NightVideoStartDate.AddDays(-1);
                        var nightEndDate = timeHelper.NightVideoEndDate.AddDays(-1);
                        // Дневной отчет: с утра сегодня до вечера сегодня
                        var dayStartDate = timeHelper.DayVideoStartDate;
                        var dayEndDate = timeHelper.DayVideoEndDate;
                        
                        // Проверяем статус отчетов перед отправкой (асинхронно)
                        var nightReportStatus = await dbConnection.GetReportStatusAsync(nightStartDate, nightEndDate);
                        var dayReportStatus = await dbConnection.GetReportStatusAsync(dayStartDate, dayEndDate);
                        
                        bool nightReportSent = nightReportStatus != null && nightReportStatus.IsSent;
                        bool dayReportSent = dayReportStatus != null && dayReportStatus.IsSent;
                        
                        if (nightReportSent && dayReportSent)
                        {
                            logger.LogInformation("Оба отчета (дневной и ночной) уже были отправлены ранее");
                            return;
                        }
                        
                        var vidNight = await videoRepository.GetVideosAsync(nightStartDate, nightEndDate);
                        var vidDay = await videoRepository.GetVideosAsync(dayStartDate, dayEndDate);

                        if (!nightReportSent)
                        {
                            await bot.SendVideoGroupAsync(vidNight, nightStartDate, nightEndDate);
                        }
                        
                        if (!dayReportSent)
                        {
                            await bot.SendVideoGroupAsync(vidDay, dayStartDate, dayEndDate);
                        }

                        // Убираем вызовы обновления дат, так как они теперь вычисляются динамически
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ошибка в методе SendVideo");
            }
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

        private CountryTelegramBot.Services.WatcherType GetWatcherType(string type)
        {
            if (type == "ASAP")
            {
                logger?.LogInformation("Выбранный метод отчета: ASAP (отправка отчетов в реальном времени)");
                return CountryTelegramBot.Services.WatcherType.ASAP;
            }
            else if (type == "Morning")
            {
                logger?.LogInformation("Выбранный метод отчета: Morning (отправка отчетов в утром)");
                return CountryTelegramBot.Services.WatcherType.Morning;
            }
            else
            {
                logger?.LogInformation("Выбранный метод отчета: MorningAndEvening (отправка отчетов в утром и вечером)");
                return CountryTelegramBot.Services.WatcherType.MorningAndEvening;
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