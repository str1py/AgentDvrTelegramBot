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
        /// <param name="targetSizeBytes">Целевой размер в байтах (по умолчанию 10 МБ)</param>
        /// <returns>Путь к сжатому видеофайлу или null, если сжатие не удалось</returns>
        public async Task<string?> CompressVideoIfNeededAsync(string inputPath, long targetSizeBytes = 10 * 1024 * 1024)
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
                
                // Пробуем сначала базовую команду FFmpeg с улучшенными параметрами качества
                var ffmpegPath = "ffmpeg"; // Предполагаем, что ffmpeg установлен и доступен в PATH
                
                // Улучшенная команда с лучшим качеством
                var arguments = $"-i \"{inputPath}\" -vcodec libx264 -crf 23 -preset medium -b:v {targetBitrate}k -maxrate {targetBitrate * 2}k -bufsize {targetBitrate * 2}k -vf scale=1280:720 -r 15 -y \"{outputPath}\"";
                
                _logger?.LogInformation($"Сжатие видео (попытка 1): {inputPath} -> {outputPath}, целевой битрейт: {targetBitrate}k");
                _logger?.LogInformation($"Команда FFmpeg: {ffmpegPath} {arguments}");
                
                var success = await ExecuteFFmpegCommand(ffmpegPath, arguments, outputPath);
                if (success)
                {
                    var compressedFileInfo = new FileInfo(outputPath);
                    _logger?.LogInformation($"Видео успешно сжато. Исходный размер: {originalSize} байт, Сжатый размер: {compressedFileInfo.Length} байт");
                    return true;
                }
                
                // Если первая попытка не удалась, пробуем альтернативную команду с лучшим качеством
                _logger?.LogWarning("Первая попытка сжатия не удалась. Пробуем альтернативную команду с лучшим качеством...");
                var alternativeArguments = $"-i \"{inputPath}\" -vcodec libx264 -crf 26 -preset fast -vf scale=854:480 -r 12 -y \"{outputPath}\"";
                
                _logger?.LogInformation($"Сжатие видео (попытка 2): {inputPath} -> {outputPath}");
                _logger?.LogInformation($"Альтернативная команда FFmpeg: {ffmpegPath} {alternativeArguments}");
                
                success = await ExecuteFFmpegCommand(ffmpegPath, alternativeArguments, outputPath);
                if (success)
                {
                    var compressedFileInfo = new FileInfo(outputPath);
                    _logger?.LogInformation($"Видео успешно сжато (альтернативная команда). Исходный размер: {originalSize} байт, Сжатый размер: {compressedFileInfo.Length} байт");
                    return true;
                }
                
                // Если все попытки не удалась, пробуем минимальное сжатие для обеспечения воспроизводимости
                _logger?.LogWarning("Обе попытки сжатия не удалась. Пробуем минимальное сжатие...");
                var minimalArguments = $"-i \"{inputPath}\" -vcodec libx264 -crf 28 -vf scale=640:360 -r 10 -y \"{outputPath}\"";
                
                _logger?.LogInformation($"Сжатие видео (попытка 3): {inputPath} -> {outputPath}");
                _logger?.LogInformation($"Минимальная команда FFmpeg: {ffmpegPath} {minimalArguments}");
                
                success = await ExecuteFFmpegCommand(ffmpegPath, minimalArguments, outputPath);
                if (success)
                {
                    var compressedFileInfo = new FileInfo(outputPath);
                    _logger?.LogInformation($"Видео успешно сжато (минимальная команда). Исходный размер: {originalSize} байт, Сжатый размер: {compressedFileInfo.Length} байт");
                    return true;
                }
                
                // Если все попытки не удалась, возвращаем false
                _logger?.LogError("Все попытки сжатия видео не удались");
                return false;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Ошибка при сжатии видео: {inputPath}");
                return false;
            }
        }
        
        /// <summary>
        /// Выполняет команду FFmpeg и возвращает результат
        /// </summary>
        /// <param name="ffmpegPath">Путь к исполняемому файлу FFmpeg</param>
        /// <param name="arguments">Аргументы командной строки</param>
        /// <param name="outputPath">Путь к выходному файлу</param>
        /// <returns>True, если команда выполнена успешно, иначе false</returns>
        private async Task<bool> ExecuteFFmpegCommand(string ffmpegPath, string arguments, string outputPath)
        {
            try
            {
                using var process = new Process();
                process.StartInfo.FileName = ffmpegPath;
                process.StartInfo.Arguments = arguments;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.CreateNoWindow = true;
                
                process.Start();
                await process.WaitForExitAsync();
                
                return process.ExitCode == 0 && File.Exists(outputPath);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Ошибка выполнения команды FFmpeg: {ffmpegPath} {arguments}");
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
                
                // Минимальный битрейт для приемлемого качества (увеличен для лучшего качества)
                var minBitrate = 800; // 800 kbps (было 500)
                
                // Максимальный битрейт (увеличен для лучшего качества)
                var maxBitrate = 4000; // 4000 kbps (4 Mbps) (было 5000)
                
                // Ограничиваем битрейт разумными пределами
                targetBitrate = Math.Max(minBitrate, Math.Min(maxBitrate, targetBitrate));
                
                _logger?.LogInformation($"Рассчитанный битрейт для {videoPath}: {targetBitrate} kbps (длительность: {durationSeconds} секунд)");
                
                return targetBitrate;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Ошибка при расчете целевого битрейта");
                // Возвращаем значение по умолчанию с лучшим качеством
                return 2500; // 2500 kbps (2.5 Mbps) (было 2000)
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
                // Пытаемся использовать FFprobe для получения точной длительности видео
                var ffmpegPath = "ffprobe"; // Используем ffprobe для получения метаданных
                var arguments = $"-v quiet -show_entries format=duration -of csv=p=0 \"{videoPath}\"";
                
                using var process = new Process();
                process.StartInfo.FileName = ffmpegPath;
                process.StartInfo.Arguments = arguments;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.CreateNoWindow = true;
                
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                
                if (process.ExitCode == 0 && double.TryParse(output.Trim(), out double duration))
                {
                    // Ограничиваем разумными пределами (от 1 секунды до 1 часа)
                    duration = Math.Max(1, Math.Min(3600, duration));
                    _logger?.LogInformation($"Точная длительность видео {videoPath}: {duration} секунд");
                    return duration;
                }
                else
                {
                    _logger?.LogWarning($"Не удалось получить точную длительность видео с ffprobe. Код выхода: {process.ExitCode}. Используется оценка.");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Ошибка при попытке получить длительность видео с ffprobe. Используется оценка.");
            }
            
            try
            {
                // Если ffprobe недоступен, используем оценку
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
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Ошибка при оценке длительности видео");
                // В случае ошибки возвращаем значение по умолчанию
                return 30; // 30 секунд
            }
        }
    }
}