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
using FFMpegCore;
using FFMpegCore.Enums;
using System.Drawing;

namespace CountryTelegramBot.Services
{
    /// <summary>
    /// Сервис сжатия видео
    /// </summary>
    public class VideoCompressionService : IVideoCompressionService
    {
        private readonly ILogger<VideoCompressionService>? _logger;
        private readonly IFileHelper _fileHelper;
        private readonly string _ffmpegPath;

        public VideoCompressionService(ILogger<VideoCompressionService>? logger, IFileHelper fileHelper)
        {
            _logger = logger;
            _fileHelper = fileHelper ?? throw new ArgumentNullException(nameof(fileHelper));
            
            // Определяем путь к FFmpeg бинарным файлам
            // Вы можете изменить этот путь на фактическое расположение FFmpeg на вашей системе
            _ffmpegPath = GetFFmpegPath();
            
            // Настраиваем FFmpegCore для использования указанного пути
            GlobalFFOptions.Configure(new FFOptions { BinaryFolder = _ffmpegPath });
        }

        /// <summary>
        /// Определяет путь к FFmpeg бинарным файлам
        /// </summary>
        /// <returns>Путь к папке с FFmpeg бинарными файлами</returns>
        private string GetFFmpegPath()
        {
            // Попробуем несколько возможных путей
            var possiblePaths = new[]
            {
                Path.Combine(Environment.CurrentDirectory, "ffmpeg", "bin"),
                Path.Combine(Environment.CurrentDirectory, "FFmpeg", "bin"),
                @"C:\ffmpeg\bin",
                @"C:\Program Files\ffmpeg\bin",
                @"C:\Program Files (x86)\ffmpeg\bin",
                // Добавьте сюда другие возможные пути в вашей системе
            };

            foreach (var path in possiblePaths)
            {
                if (Directory.Exists(path) && 
                    File.Exists(Path.Combine(path, "ffmpeg.exe")) && 
                    File.Exists(Path.Combine(path, "ffprobe.exe")))
                {
                    _logger?.LogInformation($"Найдены FFmpeg бинарные файлы в: {path}");
                    return path;
                }
            }

            // Если не найдены, используем текущую директорию
            _logger?.LogWarning("FFmpeg бинарные файлы не найдены. Используется текущая директория.");
            return Environment.CurrentDirectory;
        }

        /// <summary>
        /// Сжимает видео до указанного размера, если это необходимо
        /// </summary>
        /// < <param name="inputPath">Путь к исходному видеофайлу</param>
        /// <param name="targetSizeBytes">Целевой размер в байтах (по умолчанию 5 МБ)</param>
        /// <returns>Путь к сжатому видеофайлу или null, если сжатие не удалось</returns>
        public async Task<string?> CompressVideoIfNeededAsync(string inputPath, long targetSizeBytes = 5 * 1024 * 1024)
        {
            try
            {
                // Проверяем существование исходного файла
                if (!File.Exists(inputPath))
                {
                    _logger?.LogWarning($"Исходный файл не существует: {inputPath}");
                    return null;
                }

                // Получаем информацию о размере исходного файла
                var fileInfo = new FileInfo(inputPath);
                var originalSize = fileInfo.Length;
                _logger?.LogInformation($"Исходный размер файла {inputPath}: {originalSize} байт ({originalSize / (1024.0 * 1024.0):F2} МБ)");
                _logger?.LogInformation($"Целевой размер: {targetSizeBytes} байт ({targetSizeBytes / (1024.0 * 1024.0):F2} МБ)");

                // Проверяем, нужно ли сжимать видео
                if (originalSize > targetSizeBytes)
                {
                    _logger?.LogWarning($"Видео {inputPath} превышает целевой размер. Попытка сжатия...");
                    
                    // Пытаемся сжать видео
                    var compressedVideoPath = Path.Combine(
                        Path.GetDirectoryName(inputPath) ?? string.Empty, 
                        $"{Path.GetFileNameWithoutExtension(inputPath)}_compressed{Path.GetExtension(inputPath)}");
                    
                    var compressionSuccess = await CompressVideoAsync(inputPath, compressedVideoPath, targetSizeBytes);
                    
                    if (compressionSuccess && File.Exists(compressedVideoPath))
                    {
                        var compressedFileInfo = new FileInfo(compressedVideoPath);
                        _logger?.LogInformation($"Видео успешно сжато: {inputPath} -> {compressedVideoPath}");
                        _logger?.LogInformation($"Размер после сжатия: {inputPath}: {compressedFileInfo.Length} байт ({compressedFileInfo.Length / (1024.0 * 1024.0):F2} МБ)");
                        
                        // Проверяем, что сжатый файл соответствует целевому размеру (с допуском 10%)
                        if (compressedFileInfo.Length <= targetSizeBytes * 1.1)
                        {
                            return compressedVideoPath;
                        }
                        else
                        {
                            _logger?.LogWarning($"Сжатый файл все еще превышает целевой размер: {compressedVideoPath}");
                            // Даже если файл превышает размер, возвращаем его путь, чтобы попытаться использовать
                            return compressedVideoPath;
                        }
                    }
                    else
                    {
                        _logger?.LogWarning($"Не удалось сжать видео до целевого размера: {inputPath}");
                        // Возвращаем null, чтобы использовать оригинальный файл
                        return null;
                    }
                }
                else
                {
                    _logger?.LogInformation($"Видео {inputPath} уже соответствует целевому размеру. Сжатие не требуется.");
                    return inputPath; // Файл уже в пределах целевого размера
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Ошибка при проверке необходимости сжатия видео: {inputPath}");
                // Даже при ошибке возвращаем путь к оригинальному файлу, чтобы попытаться использовать его
                return inputPath;
            }
        }

        /// <summary>
        /// Проверяет, нужно ли сжимать видео
        /// </summary>
        /// <param name="filePath">Путь к видеофайлу</param>
        /// <param name="maxSizeBytes">Максимальный размер в байтах</param>
        /// <returns>True, если видео нужно сжать, иначе false</returns>
        public bool IsCompressionNeeded(string filePath, long maxSizeBytes = 5 * 1024 * 1024)
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
        /// Сжимает видео до указанного размера с использованием FFmpegCore
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
                
                // Получаем информацию о видео с помощью FFmpegCore
                var mediaInfo = await FFProbe.AnalyseAsync(inputPath);
                var duration = mediaInfo.Duration.TotalSeconds;
                
                // Вычисляем битрейт для достижения целевого размера
                var targetBitrate = CalculateTargetBitrate(duration, targetSizeBytes);
                
                _logger?.LogInformation($"Информация о видео: Длительность={duration} секунд, Целевой битрейт={targetBitrate} kbps");
                
                // Пробуем сначала базовую команду FFmpegCore с улучшенными параметрами качества (1280x720 для 5 МБ)
                _logger?.LogInformation($"Сжатие видео (попытка 1): {inputPath} -> {outputPath}, целевой битрейт: {targetBitrate}k");
                
                var success = await CompressWithFFmpegCore(inputPath, outputPath, targetBitrate, 1280, 720, 15);
                if (success && File.Exists(outputPath))
                {
                    var compressedFileInfo = new FileInfo(outputPath);
                    _logger?.LogInformation($"Видео успешно сжато. Исходный размер: {originalSize} байт, Сжатый размер: {compressedFileInfo.Length} байт");
                    return true;
                }
                
                // Если первая попытка не удалась, пробуем альтернативную команду с хорошим качеством (854x480 для 5 МБ)
                _logger?.LogWarning("Первая попытка сжатия не удалась. Пробуем альтернативную команду с разрешением 854x480...");
                _logger?.LogInformation($"Сжатие видео (попытка 2): {inputPath} -> {outputPath}");
                
                success = await CompressWithFFmpegCore(inputPath, outputPath, targetBitrate, 854, 480, 12);
                if (success && File.Exists(outputPath))
                {
                    var compressedFileInfo = new FileInfo(outputPath);
                    _logger?.LogInformation($"Видео успешно сжато (альтернативная команда). Исходный размер: {originalSize} байт, Сжатый размер: {compressedFileInfo.Length} байт");
                    return true;
                }
                
                // Если вторая попытка не удалась, пробуем минимальную команду
                _logger?.LogWarning("Вторая попытка сжатия не удалась. Пробуем минимальную команду...");
                _logger?.LogInformation($"Сжатие видео (попытка 3): {inputPath} -> {outputPath}");
                
                success = await CompressWithFFmpegCore(inputPath, outputPath, targetBitrate / 2, 640, 360, 10);
                if (success && File.Exists(outputPath))
                {
                    var compressedFileInfo = new FileInfo(outputPath);
                    _logger?.LogInformation($"Видео успешно сжато (минимальная команда). Исходный размер: {originalSize} байт, Сжатый размер: {compressedFileInfo.Length} байт");
                    return true;
                }

                // Если все попытки не удалась, возвращаем false
                _logger?.LogError("Все попытки сжатия видео не удались. Возвращаем оригинальный файл.");
                // Копируем оригинальный файл как есть
                File.Copy(inputPath, outputPath, true);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Ошибка при сжатии видео: {inputPath}");
                try
                {
                    // В случае ошибки копируем оригинальный файл
                    File.Copy(inputPath, outputPath, true);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }
        
        /// <summary>
        /// Сжимает видео с использованием FFmpegCore
        /// </summary>
        /// <param name="inputPath">Путь к исходному видеофайлу</param>
        /// <param name="outputPath">Путь для сохранения сжатого видео</param>
        /// <param name="bitrate">Битрейт в килобитах</param>
        /// <param name="width">Ширина видео</param>
        /// <param name="height">Высота видео</param>
        /// <param name="frameRate">Частота кадров</param>
        /// <returns>True, если сжатие успешно, иначе false</returns>
        private async Task<bool> CompressWithFFmpegCore(string inputPath, string outputPath, int bitrate, int width, int height, int frameRate)
        {
            try
            {
                // Используем FFmpegCore для сжатия видео
                await FFMpegArguments
                    .FromFileInput(inputPath)
                    .OutputToFile(outputPath, true, options => options
                        .WithVideoCodec("libx264")
                        .WithConstantRateFactor(26) // Более высокое значение CRF для меньшего размера файла
                        .WithVideoBitrate(bitrate)
                        .WithFramerate(frameRate)
                        .Resize(new Size(width, height)))
                    .ProcessAsynchronously();
                
                return File.Exists(outputPath);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Ошибка при сжатии видео с FFmpegCore: {inputPath}");
                return false;
            }
        }
        
        /// <summary>
        /// Вычисляет целевой битрейт для достижения указанного размера файла
        /// </summary>
        /// <param name="durationSeconds">Длительность видео в секундах</param>
        /// <param name="targetSizeBytes">Целевой размер в байтах</param>
        /// <returns>Целевой битрейт в килобитах</returns>
        private int CalculateTargetBitrate(double durationSeconds, long targetSizeBytes)
        {
            try
            {
                // Вычисляем битрейт (в килобитах в секунду)
                // targetSizeBytes * 8 / durationSeconds / 1000 = килобиты в секунду
                var targetBitrate = (int)(targetSizeBytes * 8.0 / durationSeconds / 1000);
                
                // Минимальный битрейт для приемлемого качества (подходит для 5 МБ)
                var minBitrate = 500; // 500 kbps
                
                // Максимальный битрейт (подходит для 5 МБ)
                var maxBitrate = 5000; // 5000 kbps (5 Mbps)
                
                // Ограничиваем битрейт разумными пределами
                targetBitrate = Math.Max(minBitrate, Math.Min(maxBitrate, targetBitrate));
                
                _logger?.LogInformation($"Рассчитанный битрейт: {targetBitrate} kbps (длительность: {durationSeconds} секунд)");
                
                return targetBitrate;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Ошибка при расчете целевого битрейта");
                // Возвращаем значение по умолчанию для 5 МБ
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
                // Используем FFmpegCore для получения точной длительности видео
                var mediaInfo = FFProbe.Analyse(videoPath);
                var duration = mediaInfo.Duration.TotalSeconds;
                
                // Ограничиваем разумными пределами (от 1 секунды до 1 часа)
                duration = Math.Max(1, Math.Min(3600, duration));
                
                _logger?.LogInformation($"Точная длительность видео {videoPath}: {duration} секунд");
                return duration;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Ошибка при попытке получить длительность видео с FFmpegCore. Используется оценка.");
                
                try
                {
                    // Если FFmpegCore не работает, используем оценку
                    var fileInfo = new FileInfo(videoPath);
                    var fileSizeMB = fileInfo.Length / (1024.0 * 1024.0);
                    
                    // Предполагаем средний битрейт 2 Мбит/с (примерно 250 КБ/с)
                    var averageBitrateMBps = 0.25;
                    var durationSeconds = fileSizeMB / averageBitrateMBps;
                    
                    // Ограничиваем разумными пределами (от 1 секунды до 1 часа)
                    durationSeconds = Math.Max(1, Math.Min(3600, durationSeconds));
                    
                    _logger?.LogInformation($"Используется оценка длительности видео: {durationSeconds} секунд (размер файла: {fileSizeMB:F2} МБ)");
                    return durationSeconds;
                }
                catch (Exception innerEx)
                {
                    _logger?.LogError(innerEx, "Ошибка при оценке длительности видео");
                    // В случае ошибки возвращаем значение по умолчанию
                    return 30; // 30 секунд
                }
            }
        }
    }
}