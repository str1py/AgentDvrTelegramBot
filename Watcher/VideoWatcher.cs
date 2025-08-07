using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Reflection.Metadata;

namespace CountryTelegramBot
{
    public class VideoWatcher
    {

        private enum WatcherType
        {
            ASAP,
            Morning,
            MorningAndEvening
        }
        private readonly List<string> folders;
        private readonly List<FileSystemWatcher> watchers;
        private readonly TelegramBot bot;
        private readonly ILogger? logger;
        private DbConnection dbConnection;
        private WatcherType watcherType;
        private bool disposed;
        private readonly TimeHelper timeHelper;
        private readonly FileHelper fileHelper;
        private readonly DailyScheduler dailyScheduler;



        public VideoWatcher(TelegramBot bot, DbConnection dbConnection, IConfigurationSection config, TimeHelper timeHelper, IEnumerable<string>? watchFolders = null,
        ILogger? logger = null)
        {
            this.bot = bot ?? throw new ArgumentNullException(nameof(bot));
            this.logger = logger;
            fileHelper = new FileHelper(logger);
            this.dbConnection = dbConnection;

            watcherType = GetWatcherType(config["WatcherType"] ?? "ASAP");

            folders = watchFolders?.ToList() ?? new List<string>();
            watchers = new List<FileSystemWatcher>();
            this.timeHelper = timeHelper;
            dailyScheduler = new DailyScheduler(SendVideo);

            foreach (string folder in folders)
            {
                var watcher = fileHelper.CreateFolderWatcher(folder);
                if (watcher != null)
                {
                    watcher.Created += OnNewVideo;
                    watchers.Add(watcher);
                    logger?.LogInformation($"Мониторю {folder}...");
                }
            }
        }

        private async void OnNewVideo(object sender, FileSystemEventArgs e)
        {
            try
            {
                await Task.Delay(20000); // Ждем, пока файл полностью запишется
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
                logger?.LogError(ex, "Ошибка при обработке нового видео");
            }
        }


        private async void SendVideo(object state)
        {
            var now = DateTime.Now;
            logger?.LogInformation($"{DateTime.Now.ToShortTimeString()}: Here is a tick in SendVideo");
            if (watcherType == WatcherType.Morning)
            {
                if (now >= timeHelper.morningReport)
                {
                    var vid = dbConnection.GetVideos(timeHelper.NightVideoStartDate, timeHelper.NightVideoEndDate);
                    await bot.SendVideoGroupAsync(vid, timeHelper.NightVideoStartDate, timeHelper.NightVideoEndDate);
                    timeHelper.CalculateNextNightPeriod();
                    timeHelper.CalculateNextMorningReport();
                }
                    
                //logger?.LogInformation($"Следующий утренний отчет: {timeHelper.morningReport.ToShortDateString()} {timeHelper.morningReport.ToShortTimeString()}");
                
            }
            else if (watcherType == WatcherType.MorningAndEvening)
            {
                if (now >= timeHelper.eveningReport)
                {
                    var vidNight = dbConnection.GetVideos(timeHelper.NightVideoStartDate, timeHelper.NightVideoEndDate);
                    var vidDay = dbConnection.GetVideos(timeHelper.DayVideoStartDate, timeHelper.DayVideoEndDate);

                    await bot.SendVideoGroupAsync(vidDay, timeHelper.DayVideoStartDate, timeHelper.DayVideoEndDate);
                    await bot.SendVideoGroupAsync(vidNight, timeHelper.NightVideoStartDate, timeHelper.NightVideoEndDate);

                    timeHelper.CalculateNextDayPeriod();
                    timeHelper.CalculateNextNightPeriod();
                    timeHelper.CalculateNextMorningReport();
                    timeHelper.CalculateNextEveningReport();
                }
                    //logger?.LogInformation($"Следующий утренний отчет: {timeHelper.morningReport.ToShortDateString()} {timeHelper.morningReport.ToShortTimeString()}");
                    //logger?.LogInformation($"Следующий вечерний отчет: {timeHelper.eveningReport.ToShortDateString()} {timeHelper.eveningReport.ToShortTimeString()}");               
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
                watcher.Created -= OnNewVideo;
                watcher.Dispose();
            }
            disposed = true;
        }
    }
}