namespace CountryTelegramBot.Models
{
    public interface IFileHelper
    {
        Task<FileStream?> GetFileStreamFromVideo(string videoPath, int maxAttempts = 30, int delayMs = 1000);
        bool IsFileLocked(IOException ex);
        FileSystemWatcher? CreateFolderWatcher(string folder);
        
        /// <summary>
        /// Проверяет, не превышает ли размер файла допустимый лимит для Telegram
        /// </summary>
        /// <param name="filePath">Путь к файлу</param>
        /// <param name="maxSizeBytes">Максимальный размер в байтах (по умолчанию 50 МБ для Telegram)</param>
        /// <returns>True, если файл не превышает лимит, иначе false</returns>
        bool IsFileSizeWithinLimit(string filePath, long maxSizeBytes = 50 * 1024 * 1024);
    }
}