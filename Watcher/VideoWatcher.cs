using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Reflection.Metadata;

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
        private readonly ITelegramBot bot;
        private readonly ILogger? logger;
        private readonly IDbConnection dbConnection;
        private WatcherType watcherType;
        private bool disposed;
        private readonly ITimeHelper timeHelper;
        private readonly IFileHelper fileHelper;
        private readonly IDailyScheduler dailyScheduler;



        public VideoWatcher(TelegramBot bot, DbConnection dbConnection, CountryTelegramBot.Configs.CommonConfig config, TimeHelper timeHelper, IEnumerable<string>? watchFolders = null,
        ILogger? logger = null)
        {
            this.bot = bot ?? throw new ArgumentNullException(nameof(bot));
            this.logger = logger;
            this.fileHelper = fileHelper ?? throw new ArgumentNullException(nameof(fileHelper));
            this.dbConnection = dbConnection ?? throw new ArgumentNullException(nameof(dbConnection));
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
                await Task.Delay(20000); // Ждём, пока файл полностью запишется
                logger?.LogInformation($"{e.ChangeType}: {e.FullPath}");
                var path = e.FullPath;
                var grabsPath = GetDirectoryFromFilePath(path);
                var grab = GetLastGrab(grabsPath);
                if (grab == null)
                {
                    logger?.LogWarning($"Не найдено изображение для превью в {grabsPath}");
                    return;
                }

                await dbConnection.AddVideoData(path, grab);
                logger?.LogInformation($"В базу данных добавлена новая запись ({Path.GetFileName(path)})");

                if (watcherType == WatcherType.ASAP)
                {
                    var video = await dbConnection.GetLastVideo();
                    if (video is not null)
                        await bot.SendVideoSafely(video?.Path ?? "empty", video?.Grab ?? "empty");
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, $"Ошибка обработки нового видеофайла: {e.FullPath}");
            }

        }


        private void SendVideo(object? state)
        {
            var now = DateTime.Now;
            logger?.LogInformation($"{DateTime.Now.ToShortTimeString()}: Here is a tick in SendVideo");
            if (watcherType == WatcherType.Morning)
            {
                if (now >= timeHelper.MorningReport)
                {
                    var vid = dbConnection.GetVideos(timeHelper.NightVideoStartDate, timeHelper.NightVideoEndDate);
                    bot.SendVideoGroupAsync(vid, timeHelper.NightVideoStartDate, timeHelper.NightVideoEndDate).Wait();
                    timeHelper.CalculateNextNightPeriod();
                    timeHelper.CalculateNextMorningReport();
                }
            }
            else if (watcherType == WatcherType.MorningAndEvening)
            {
                if (now >= timeHelper.EveningReport)
                {
                    var vidNight = dbConnection.GetVideos(timeHelper.NightVideoStartDate, timeHelper.NightVideoEndDate);
                    var vidDay = dbConnection.GetVideos(timeHelper.DayVideoStartDate, timeHelper.DayVideoEndDate);

                    bot.SendVideoGroupAsync(vidDay, timeHelper.DayVideoStartDate, timeHelper.DayVideoEndDate).Wait();
                    bot.SendVideoGroupAsync(vidNight, timeHelper.NightVideoStartDate, timeHelper.NightVideoEndDate).Wait();

                    timeHelper.CalculateNextDayPeriod();
                    timeHelper.CalculateNextNightPeriod();
                    timeHelper.CalculateNextMorningReport();
                    timeHelper.CalculateNextEveningReport();
                }
            }
        }

        private string? GetDirectoryFromFilePath(string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath))
                    return null;
                string? directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    if (!directory.EndsWith(Path.DirectorySeparatorChar.ToString()))
                        directory += Path.DirectorySeparatorChar;
                    return Path.Combine(directory, "grabs");
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Ошибка обработки пути");
            }
            return null;
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
            if (disposed) return;
            foreach (var watcher in watchers)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Created -= async (s, e) => await OnNewVideo(s, e); ;
                watcher.Dispose();
            }
            disposed = true;
        }
    }
}