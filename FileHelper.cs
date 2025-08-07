using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace CountryTelegramBot
{
    public class FileHelper
    {

        private readonly ILogger? logger;
        public FileHelper(ILogger? logger)
        {
            this.logger = logger;
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
                    FileShare.ReadWrite, // Разрешаем другим читать, но не изменять
                    bufferSize: 4096,
                    useAsync: true);

                    Console.WriteLine($"Файл доступен ({attempt}/{maxAttempts})");
                    return fileStream;
                }
                catch (IOException ex) when (IsFileLocked(ex))
                {
                    Console.WriteLine($"Файл заблокирован, попытка {attempt}/{maxAttempts}");
                    await Task.Delay(delayMs * attempt); // Прогрессивная задержка
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка: {ex.Message}");
                    return null;
                }
                Console.WriteLine($"Не удалось получить доступ к файлу после {maxAttempts} попыток");
                return null;
            }
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
