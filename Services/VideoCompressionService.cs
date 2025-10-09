using Microsoft.Extensions.Logging;
using CountryTelegramBot.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace CountryTelegramBot.Services
{
    /// <summary>
    /// Сервис сжатия видео
    /// </summary>
    public class VideoCompressionService : IVideoCompressionService
    {
        private readonly ILogger<VideoCompressionService>? _logger;
        private readonly IFileHelper _fileHelper;

        public VideoCompressionService(ILogger<VideoCompressionService>? logger, IFileHelper fileHelper)
        {
            _logger = logger;
            _fileHelper = fileHelper ?? throw new ArgumentNullException(nameof(fileHelper));
        }

        /// <summary>
        /// Сжимает видео до указанного размера, если это необходимо
        /// </summary>
        /// <param name="inputPath">Путь к исходному видеофайлу</param>
        /// <param name="targetSizeBytes">Целевой размер в байтах (по умолчанию 50 МБ для Telegram)</param>
        /// <returns>Путь к сжатому видеофайлу или null, если сжатие не удалось</returns>
        public async Task<string?> CompressVideoIfNeededAsync(string inputPath, long targetSizeBytes = 50 * 1024 * 1024)
        {
            try
            {
                // Проверяем существование исходного файла
                if (!File.Exists(inputPath))
                {
                    _logger?.LogWarning($"Исходный файл не существует: {inputPath}");
                    return null;
                }

                // Проверяем, нужно ли сжимать видео
                if (!_fileHelper.IsFileSizeWithinLimit(inputPath, targetSizeBytes))
                {
                    _logger?.LogWarning($"Видео {inputPath} превышает допустимый размер. Попытка сжатия...");
                    
                    // Пытаемся сжать видео
                    var compressedVideoPath = Path.Combine(
                        Path.GetDirectoryName(inputPath) ?? string.Empty, 
                        $"{Path.GetFileNameWithoutExtension(inputPath)}_compressed{Path.GetExtension(inputPath)}");
                    
                    var compressionSuccess = await CompressVideoAsync(inputPath, compressedVideoPath, targetSizeBytes);
                    
                    if (compressionSuccess && _fileHelper.IsFileSizeWithinLimit(compressedVideoPath, targetSizeBytes))
                    {
                        _logger?.LogInformation($"Видео успешно сжато: {inputPath} -> {compressedVideoPath}");
                        return compressedVideoPath;
                    }
                    else
                    {
                        _logger?.LogWarning($"Не удалось сжать видео до допустимого размера: {inputPath}");
                        
                        // Удаляем временный сжатый файл, если он был создан
                        if (File.Exists(compressedVideoPath))
                            File.Delete(compressedVideoPath);
                            
                        return null;
                    }
                }
                else
                {
                    _logger?.LogInformation($"Видео {inputPath} уже соответствует допустимому размеру. Сжатие не требуется.");
                    return inputPath; // Файл уже в пределах допустимого размера
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Ошибка при проверке необходимости сжатия видео: {inputPath}");
                return null;
            }
        }

        /// <summary>
        /// Проверяет, нужно ли сжимать видео
        /// </summary>
        /// <param name="filePath">Путь к видеофайлу</param>
        /// <param name="maxSizeBytes">Максимальный размер в байтах</param>
        /// <returns>True, если видео нужно сжать, иначе false</returns>
        public bool IsCompressionNeeded(string filePath, long maxSizeBytes = 50 * 1024 * 1024)
        {
            try
            {
                return !_fileHelper.IsFileSizeWithinLimit(filePath, maxSizeBytes);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Ошибка при проверке необходимости сжатия видео: {filePath}");
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
        private async Task<bool> CompressVideoAsync(string inputPath, string outputPath, long targetSizeBytes)
        {
            try
            {
                // Проверяем существование исходного файла
                if (!File.Exists(inputPath))
                {
                    _logger?.LogWarning($"Исходный файл не существует: {inputPath}");
                    return false;
                }
                
                // Получаем информацию о видео
                var fileInfo = new FileInfo(inputPath);
                var originalSize = fileInfo.Length;
                
                // Если файл уже меньше целевого размера, копируем его как есть
                if (originalSize <= targetSizeBytes)
                {
                    File.Copy(inputPath, outputPath, true);
                    _logger?.LogInformation($"Файл {inputPath} уже меньше целевого размера. Скопирован как есть.");
                    return true;
                }
                
                // Вычисляем битрейт для достижения целевого размера
                var targetBitrate = CalculateTargetBitrate(inputPath, targetSizeBytes);
                
                // Команда FFmpeg для сжатия видео
                var ffmpegPath = "ffmpeg"; // Предполагаем, что ffmpeg установлен и доступен в PATH
                var arguments = $"-i \"{inputPath}\" -b:v {targetBitrate}k -bufsize {targetBitrate * 2}k -maxrate {targetBitrate * 2}k -vf scale=1280:720 -preset fast -y \"{outputPath}\"";
                
                _logger?.LogInformation($"Сжатие видео: {inputPath} -> {outputPath}, целевой битрейт: {targetBitrate}k");
                
                // Выполняем команду FFmpeg
                using var process = new Process();
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
                    _logger?.LogInformation($"Видео успешно сжато. Исходный размер: {originalSize} байт, Сжатый размер: {compressedFileInfo.Length} байт");
                    return true;
                }
                else
                {
                    var errorOutput = await process.StandardError.ReadToEndAsync();
                    _logger?.LogError($"Ошибка сжатия видео. Код выхода: {process.ExitCode}, Ошибка: {errorOutput}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Ошибка при сжатии видео: {inputPath}");
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
                // Получаем длительность видео
                var durationSeconds = GetVideoDuration(videoPath);
                
                // Вычисляем битрейт (в килобитах в секунду)
                // targetSizeBytes * 8 / durationSeconds / 1000 = килобиты в секунду
                var targetBitrate = (int)(targetSizeBytes * 8.0 / durationSeconds / 1000);
                
                // Минимальный битрейт для приемлемого качества
                var minBitrate = 500; // 500 kbps
                
                // Максимальный битрейт (чтобы не делать видео слишком плохого качества)
                var maxBitrate = 5000; // 5000 kbps (5 Mbps)
                
                // Ограничиваем битрейт разумными пределами
                targetBitrate = Math.Max(minBitrate, Math.Min(maxBitrate, targetBitrate));
                
                _logger?.LogInformation($"Рассчитанный битрейт для {videoPath}: {targetBitrate} kbps (длительность: {durationSeconds} секунд)");
                
                return targetBitrate;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Ошибка при расчете целевого битрейта");
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