using CountryTelegramBot.Models;

namespace CountryTelegramBot.Models
{
    public interface IDbConnection
    {
        bool IsConnected { get; }
        
        Task AddVideoData(string path, string grab);
        List<VideoModel> GetVideos(DateTime startDate, DateTime endDate);
        Task<List<VideoModel>> GetVideosAsync(DateTime startDate, DateTime endDate);
        Task<VideoModel> GetLastVideo();
        Task<bool> RemoveItemByPath(string path);
        List<VideoModel> GetBrokenVideos();
        
        // Методы для работы со статусом отчетов
        Task AddReportStatus(DateTime startDate, DateTime endDate, bool isSent, string? errorMessage = null);
        Task UpdateReportStatus(int id, bool isSent, string? errorMessage = null);
        List<ReportStatusModel> GetUnsentReports();
        ReportStatusModel? GetReportStatus(DateTime startDate, DateTime endDate);
        Task<ReportStatusModel?> GetReportStatusAsync(DateTime startDate, DateTime endDate);
    }
}