using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CountryTelegramBot.Services
{
    /// <summary>
    /// Интерфейс сервиса сжатия видео
    /// </summary>
    public interface IVideoCompressionService
    {
        /// <summary>
        /// Сжимает видео до указанного размера
        /// </summary>
        /// <param name="inputPath">Путь к исходному видеофайлу</param>
        /// <param name="targetSizeBytes">Целевой размер в байтах (по умолчанию 5 МБ)</param>
        /// <returns>Путь к сжатому видеофайлу или null, если сжатие не удалось</returns>
        System.Threading.Tasks.Task<string?> CompressVideoIfNeededAsync(string inputPath, long targetSizeBytes = 5 * 1024 * 1024);
        
        /// <summary>
        /// Проверяет, нужно ли сжимать видео
        /// </summary>
        /// <param name="filePath">Путь к видеофайлу</param>
        /// <param name="maxSizeBytes">Максимальный размер в байтах (по умолчанию 5 МБ)</param>
        /// <returns>True, если видео нужно сжать, иначе false</returns>
        bool IsCompressionNeeded(string filePath, long maxSizeBytes = 5 * 1024 * 1024);
    }
}