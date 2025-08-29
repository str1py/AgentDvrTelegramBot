namespace CountryTelegramBot.Models
{
    public interface IFileHelper
    {
        Task<FileStream?> GetFileStreamFromVideo(string videoPath, int maxAttempts = 30, int delayMs = 1000);
        bool IsFileLocked(IOException ex);
        FileSystemWatcher? CreateFolderWatcher(string folder);
    }
}
