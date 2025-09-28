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
        public void StartWatching()
        {
            foreach (var watcher in watchers)
            {
                watcher.EnableRaisingEvents = true;
            }
            logger?.LogInformation("Watching started.");
        }

        public void StopWatching()
        {
            foreach (var watcher in watchers)
            {
                watcher.EnableRaisingEvents = false;
            }
            logger?.LogInformation("Watching stopped.");
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



        public VideoWatcher(ITelegramBotService bot, IVideoRepository videoRepository, CountryTelegramBot.Configs.CommonConfig config, ITimeHelper timeHelper, IFileHelper fileHelper, IEnumerable<string>? watchFolders = null,
        ILogger<VideoWatcher>? logger = null)
        {
            this.bot = bot ?? throw new ArgumentNullException(nameof(bot));
            this.logger = logger;
            this.fileHelper = fileHelper ?? throw new ArgumentNullException(nameof(fileHelper));
            this.videoRepository = videoRepository ?? throw new ArgumentNullException(nameof(videoRepository));
            this.timeHelper = timeHelper ?? throw new ArgumentNullException(nameof(timeHelper));

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


        private async void SendVideo(object? state)
        {
            try
            {
                var now = DateTime.Now;
                logger?.LogInformation($"{DateTime.Now.ToShortTimeString()}: Here is a tick in SendVideo");
                if (watcherType == WatcherType.Morning)
                {
                    if (now >= timeHelper.MorningReport)
                    {
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