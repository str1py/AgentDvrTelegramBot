using CountryTelegramBot.Models;
using CountryTelegramBot.Services;
using Microsoft.Extensions.Logging;

namespace CountryTelegramBot.Repositories
{
    public class InMemoryVideoRepository : IVideoRepository
    {
        private readonly ILogger<InMemoryVideoRepository> _logger;

        public InMemoryVideoRepository(ILogger<InMemoryVideoRepository> logger)
        {
            _logger = logger;
        }

        public Task AddVideoAsync(string path, string grab)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(grab))
                return Task.CompletedTask;

            lock (InMemoryStorage.Sync)
            {
                InMemoryStorage.Videos.Add(new VideoModel
                {
                    Path = path,
                    Grab = grab,
                    Date = DateTime.Now
                });
            }

            _logger.LogWarning("Using in-memory video repository. Persisted only for current process: {Path}", path);
            return Task.CompletedTask;
        }

        public Task<List<VideoModel>> GetVideosAsync(DateTime startDate, DateTime endDate)
        {
            lock (InMemoryStorage.Sync)
            {
                return Task.FromResult(InMemoryStorage.Videos
                    .Where(v => v.Date >= startDate && v.Date <= endDate)
                    .OrderBy(v => v.Date)
                    .Select(v => new VideoModel { Id = v.Id, Path = v.Path, Grab = v.Grab, Date = v.Date })
                    .ToList());
            }
        }

        public Task<VideoModel?> GetLastVideoAsync()
        {
            lock (InMemoryStorage.Sync)
            {
                var video = InMemoryStorage.Videos
                    .OrderByDescending(v => v.Date)
                    .FirstOrDefault();

                if (video == null)
                    return Task.FromResult<VideoModel?>(null);

                return Task.FromResult<VideoModel?>(new VideoModel
                {
                    Id = video.Id,
                    Path = video.Path,
                    Grab = video.Grab,
                    Date = video.Date
                });
            }
        }

        public Task<bool> RemoveByPathAsync(string path)
        {
            lock (InMemoryStorage.Sync)
            {
                var removed = InMemoryStorage.Videos.RemoveAll(v => v.Path == path) > 0;
                return Task.FromResult(removed);
            }
        }
    }
}
