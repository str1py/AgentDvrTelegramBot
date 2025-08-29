using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace CountryTelegramBot
{
    using CountryTelegramBot.Models;

    public class FileHelper : IFileHelper
    {
        private readonly ILogger? logger;
        private readonly IErrorHandler? errorHandler;

        public FileHelper(ILogger? logger, IErrorHandler? errorHandler = null)
        {
            this.logger = logger;
            this.errorHandler = errorHandler;
        }

        public async Task<FileStream?> GetFileStreamFromVideo(string videoPath, int maxAttempts = 30, int delayMs = 1000)
        {
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    // Проверяем существование файла
                    if (!File.Exists(videoPath))
                    {
                        logger?.LogWarning($"Файл не существует: {videoPath}");
                        return null;
                    }

                    // Пытаемся открыть файл с эксклюзивным доступом
                    var fileStream = new FileStream(
                        videoPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite,
                        bufferSize: 4096,
                        useAsync: true);

                    logger?.LogInformation($"Файл доступен ({attempt}/{maxAttempts})");
                    return fileStream;
                }
                catch (IOException ex) when (IsFileLocked(ex))
                {
                    logger?.LogWarning($"Файл заблокирован, попытка {attempt}/{maxAttempts}");
                    errorHandler?.HandleError(ex, $"File locked: {videoPath}");
                    await Task.Delay(delayMs * attempt);
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, $"Ошибка доступа к файлу: {videoPath}");
                    errorHandler?.HandleError(ex, $"Ошибка доступа к файлу: {videoPath}");
                    return null;
                }
            }
            logger?.LogError($"Не удалось получить доступ к файлу после {maxAttempts} попыток: {videoPath}");
            return null;
        }
        public bool IsFileLocked(IOException ex)
        {
            int errorCode = Marshal.GetHRForException(ex) & 0xFFFF;
            return errorCode == 32 || errorCode == 33; // ERROR_SHARING_VIOLATION или ERROR_LOCK_VIOLATION
        }

        public FileSystemWatcher CreateFolderWatcher(string folder)
        {
            if (!Directory.Exists(folder))
            {
                logger?.LogWarning($"Папка не найдена: {folder}");
                errorHandler?.HandleError(new DirectoryNotFoundException(folder), $"Папка не найдена: {folder}");
                return null;
            }
            else
            {
                return new FileSystemWatcher
                {
                    Path = folder,
                    Filter = "*.mp4",
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                    EnableRaisingEvents = true
                };
            }
        
        }
    }
}
