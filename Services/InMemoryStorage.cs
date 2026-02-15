using CountryTelegramBot.Models;

namespace CountryTelegramBot.Services
{
    internal static class InMemoryStorage
    {
        internal static readonly object Sync = new();
        internal static readonly List<VideoModel> Videos = new();
        internal static readonly List<ReportStatusModel> ReportStatuses = new();
        internal static int NextReportStatusId = 1;
    }
}
