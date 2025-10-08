using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CountryTelegramBot.Models
{
    public interface IDbConnection
    {
        Task AddVideoData(string path, string grab);
        List<VideoModel> GetVideos(DateTime startDate, DateTime endDate);
        Task<List<VideoModel>> GetVideosAsync(DateTime startDate, DateTime endDate); // Add this line
        Task<VideoModel> GetLastVideo();
        Task<bool> RemoveItemByPath(string path);
        bool IsConnected { get; }
        
        // Report Status Methods
        Task AddReportStatus(DateTime startDate, DateTime endDate, bool isSent, string? errorMessage = null);
        Task UpdateReportStatus(int id, bool isSent, string? errorMessage = null);
        List<ReportStatusModel> GetUnsentReports();
        ReportStatusModel? GetReportStatus(DateTime startDate, DateTime endDate);
        Task<ReportStatusModel?> GetReportStatusAsync(DateTime startDate, DateTime endDate);
    }
}