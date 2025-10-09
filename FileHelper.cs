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
        
        /// <summary>
        /// Сжимает видео до указанного размера с использованием FFmpeg
        /// </summary>
        /// <param name="inputPath">Путь к исходному видеофайлу</param>
        /// <param name="outputPath">Путь для сохранения сжатого видео</param>
        /// <param name="targetSizeBytes">Целевой размер в байтах</param>
        /// <returns>True, если сжатие успешно, иначе false</returns>
        public async Task<bool> CompressVideoAsync(string inputPath, string outputPath, long targetSizeBytes)
        {
            try
            {
                // Проверяем существование исходного файла
                if (!File.Exists(inputPath))
                {
                    logger?.LogWarning($"Исходный файл не существует: {inputPath}");
                    return false;
                }
                
                // Получаем информацию о видео
                var fileInfo = new FileInfo(inputPath);
                var originalSize = fileInfo.Length;
                
                // Если файл уже меньше целевого размера, копируем его как есть
                if (originalSize <= targetSizeBytes)
                {
                    File.Copy(inputPath, outputPath, true);
                    logger?.LogInformation($"Файл {inputPath} уже меньше целевого размера. Скопирован как есть.");
                    return true;
                }
                
                // Вычисляем битрейт для достижения целевого размера
                // Это приблизительный расчет, может потребоваться корректировка
                var targetBitrate = CalculateTargetBitrate(inputPath, targetSizeBytes);
                
                // Команда FFmpeg для сжатия видео
                var ffmpegPath = "ffmpeg"; // Предполагаем, что ffmpeg установлен и доступен в PATH
                var arguments = $"-i \"{inputPath}\" -b:v {targetBitrate}k -bufsize {targetBitrate * 2}k -maxrate {targetBitrate * 2}k -vf scale=1280:720 -preset fast -y \"{outputPath}\"";
                
                logger?.LogInformation($"Сжатие видео: {inputPath} -> {outputPath}, целевой битрейт: {targetBitrate}k");
                
                // Выполняем команду FFmpeg
                using var process = new System.Diagnostics.Process();
                process.StartInfo.FileName = ffmpegPath;
                process.StartInfo.Arguments = arguments;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.CreateNoWindow = true;
                
                process.Start();
                await process.WaitForExitAsync();
                
                // Проверяем результат
                if (process.ExitCode == 0 && File.Exists(outputPath))
                {
                    var compressedFileInfo = new FileInfo(outputPath);
                    logger?.LogInformation($"Видео успешно сжато. Исходный размер: {originalSize} байт, Сжатый размер: {compressedFileInfo.Length} байт");
                    return true;
                }
                else
                {
                    var errorOutput = await process.StandardError.ReadToEndAsync();
                    logger?.LogError($"Ошибка сжатия видео. Код выхода: {process.ExitCode}, Ошибка: {errorOutput}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, $"Ошибка при сжатии видео: {inputPath}");
                return false;
            }
        }
        
        /// <summary>
        /// Вычисляет целевой битрейт для достижения указанного размера файла
        /// </summary>
        /// <param name="videoPath">Путь к видеофайлу</param>
        /// <param name="targetSizeBytes">Целевой размер в байтах</param>
        /// <returns>Целевой битрейт в килобитах</returns>
        private int CalculateTargetBitrate(string videoPath, long targetSizeBytes)
        {
            try
            {
                // Получаем длительность видео (простая оценка)
                // В реальной реализации лучше использовать библиотеку для получения точной длительности
                var fileInfo = new FileInfo(videoPath);
                var durationSeconds = GetVideoDuration(videoPath); // Реализация ниже
                
                // Вычисляем битрейт (в килобитах в секунду)
                // targetSizeBytes * 8 / durationSeconds / 1000 = килобиты в секунду
                var targetBitrate = (int)(targetSizeBytes * 8.0 / durationSeconds / 1000);
                
                // Минимальный битрейт для приемлемого качества
                var minBitrate = 500; // 500 kbps
                
                // Максимальный битрейт (чтобы не делать видео слишком плохого качества)
                var maxBitrate = 5000; // 5000 kbps (5 Mbps)
                
                // Ограничиваем битрейт разумными пределами
                targetBitrate = Math.Max(minBitrate, Math.Min(maxBitrate, targetBitrate));
                
                logger?.LogInformation($"Рассчитанный битрейт для {videoPath}: {targetBitrate} kbps (длительность: {durationSeconds} секунд)");
                
                return targetBitrate;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Ошибка при расчете целевого битрейта");
                // Возвращаем значение по умолчанию
                return 2000; // 2000 kbps (2 Mbps)
            }
        }
        
        /// <summary>
        /// Получает приблизительную длительность видео в секундах
        /// </summary>
        /// <param name="videoPath">Путь к видеофайлу</param>
        /// <returns>Длительность в секундах</returns>
        private double GetVideoDuration(string videoPath)
        {
            try
            {
                // Простая оценка на основе размера файла и среднего битрейта
                // В реальной реализации лучше использовать библиотеку для получения точной длительности
                var fileInfo = new FileInfo(videoPath);
                var fileSizeMB = fileInfo.Length / (1024.0 * 1024.0);
                
                // Предполагаем средний битрейт 2 Мбит/с (примерно 250 КБ/с)
                var averageBitrateMBps = 0.25;
                var durationSeconds = fileSizeMB / averageBitrateMBps;
                
                // Ограничиваем разумными пределами (от 1 секунды до 1 часа)
                durationSeconds = Math.Max(1, Math.Min(3600, durationSeconds));
                
                return durationSeconds;
            }
            catch
            {
                // В случае ошибки возвращаем значение по умолчанию
                return 60; // 1 минута
            }
        }
    }
}
