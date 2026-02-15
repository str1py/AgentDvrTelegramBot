using CountryTelegramBot.Models;
using Microsoft.Extensions.Logging;

namespace CountryTelegramBot.Services
{
    public class InMemoryDbConnection : IDbConnection
    {
        private readonly ILogger<InMemoryDbConnection> _logger;

        public InMemoryDbConnection(ILogger<InMemoryDbConnection> logger)
        {
            _logger = logger;
            _logger.LogWarning("Using in-memory DB connection. Data will be lost on restart.");
        }

        public bool IsConnected => false;

        public Task AddVideoData(string path, string grab)
        {
            if (string.IsNullOrWhiteSpace(path))
                return Task.CompletedTask;

            lock (InMemoryStorage.Sync)
            {
                InMemoryStorage.Videos.Add(new VideoModel
                {
                    Path = path,
                    Grab = grab ?? string.Empty,
                    Date = DateTime.Now
                });
            }

            return Task.CompletedTask;
        }

        public List<VideoModel> GetVideos(DateTime startDate, DateTime endDate)
        {
            lock (InMemoryStorage.Sync)
            {
                return InMemoryStorage.Videos
                    .Where(v => v.Date >= startDate && v.Date <= endDate)
                    .OrderBy(v => v.Date)
                    .Select(v => new VideoModel { Id = v.Id, Path = v.Path, Grab = v.Grab, Date = v.Date })
                    .ToList();
            }
        }

        public Task<List<VideoModel>> GetVideosAsync(DateTime startDate, DateTime endDate)
            => Task.FromResult(GetVideos(startDate, endDate));

        public Task<VideoModel> GetLastVideo()
        {
            lock (InMemoryStorage.Sync)
            {
                var video = InMemoryStorage.Videos
                    .OrderByDescending(v => v.Date)
                    .FirstOrDefault();
                return Task.FromResult(video ?? new VideoModel());
            }
        }

        public Task<bool> RemoveItemByPath(string path)
        {
            lock (InMemoryStorage.Sync)
            {
                var removed = InMemoryStorage.Videos.RemoveAll(v => v.Path == path) > 0;
                return Task.FromResult(removed);
            }
        }

        public List<VideoModel> GetBrokenVideos()
        {
            lock (InMemoryStorage.Sync)
            {
                return InMemoryStorage.Videos
                    .Where(v => string.IsNullOrWhiteSpace(v.Path) || !File.Exists(v.Path))
                    .Select(v => new VideoModel { Id = v.Id, Path = v.Path, Grab = v.Grab, Date = v.Date })
                    .ToList();
            }
        }

        public Task AddReportStatus(DateTime startDate, DateTime endDate, bool isSent, string? errorMessage = null)
        {
            lock (InMemoryStorage.Sync)
            {
                InMemoryStorage.ReportStatuses.Add(new ReportStatusModel
                {
                    Id = InMemoryStorage.NextReportStatusId++,
                    StartDate = startDate,
                    EndDate = endDate,
                    IsSent = isSent,
                    AttemptedAt = DateTime.Now,
                    SentAt = isSent ? DateTime.Now : null,
                    ErrorMessage = errorMessage,
                    SendAttempts = 1
                });
            }

            return Task.CompletedTask;
        }

        public Task UpdateReportStatus(int id, bool isSent, string? errorMessage = null)
        {
            lock (InMemoryStorage.Sync)
            {
                var item = InMemoryStorage.ReportStatuses.FirstOrDefault(r => r.Id == id);
                if (item != null)
                {
                    item.IsSent = isSent;
                    item.AttemptedAt = DateTime.Now;
                    item.SentAt = isSent ? DateTime.Now : item.SentAt;
                    item.ErrorMessage = errorMessage;
                    item.SendAttempts++;
                }
            }

            return Task.CompletedTask;
        }

        public List<ReportStatusModel> GetUnsentReports()
        {
            lock (InMemoryStorage.Sync)
            {
                return InMemoryStorage.ReportStatuses
                    .Where(r => !r.IsSent && r.SendAttempts <= 3)
                    .Select(r => new ReportStatusModel
                    {
                        Id = r.Id,
                        StartDate = r.StartDate,
                        EndDate = r.EndDate,
                        IsSent = r.IsSent,
                        AttemptedAt = r.AttemptedAt,
                        SentAt = r.SentAt,
                        ErrorMessage = r.ErrorMessage,
                        SendAttempts = r.SendAttempts
                    })
                    .ToList();
            }
        }

        public ReportStatusModel? GetReportStatus(DateTime startDate, DateTime endDate)
        {
            lock (InMemoryStorage.Sync)
            {
                var item = InMemoryStorage.ReportStatuses
                    .FirstOrDefault(r => r.StartDate == startDate && r.EndDate == endDate);

                if (item == null) return null;

                return new ReportStatusModel
                {
                    Id = item.Id,
                    StartDate = item.StartDate,
                    EndDate = item.EndDate,
                    IsSent = item.IsSent,
                    AttemptedAt = item.AttemptedAt,
                    SentAt = item.SentAt,
                    ErrorMessage = item.ErrorMessage,
                    SendAttempts = item.SendAttempts
                };
            }
        }

        public Task<ReportStatusModel?> GetReportStatusAsync(DateTime startDate, DateTime endDate)
            => Task.FromResult(GetReportStatus(startDate, endDate));
    }
}
