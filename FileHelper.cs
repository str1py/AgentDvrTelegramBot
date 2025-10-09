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
                    Filter = "*.*", // Отслеживаем все файлы
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                    EnableRaisingEvents = false // Устанавливаем в false, так как StartWatching() включит его
                };
            }
        }
        
        /// <summary>
        /// Проверяет, не превышает ли размер файла допустимый лимит для Telegram
        /// </summary>
        /// <param name="filePath">Путь к файлу</param>
        /// <param name="maxSizeBytes">Максимальный размер в байтах (по умолчанию 50 МБ для Telegram)</param>
        /// <returns>True, если файл не превышает лимит, иначе false</returns>
        public bool IsFileSizeWithinLimit(string filePath, long maxSizeBytes = 50 * 1024 * 1024)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    logger?.LogWarning($"Файл не существует: {filePath}");
                    return false;
                }
                
                var fileInfo = new FileInfo(filePath);
                var fileSize = fileInfo.Length;
                
                if (fileSize > maxSizeBytes)
                {
                    logger?.LogWarning($"Файл {filePath} превышает допустимый размер. Размер: {fileSize} байт, Максимум: {maxSizeBytes} байт");
                    return false;
                }
                
                return true;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, $"Ошибка при проверке размера файла: {filePath}");
                return false;
            }
        }
    }
}
