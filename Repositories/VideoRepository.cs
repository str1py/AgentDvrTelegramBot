using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CountryTelegramBot.Repositories
{
    /// <summary>
    /// Репозиторий для работы с видеозаписями в базе данных
    /// </summary>
    public class VideoRepository : IVideoRepository
    {
        private readonly DbCountryContext _context;
        private readonly ILogger<VideoRepository> _logger;

        public VideoRepository(DbCountryContext context, ILogger<VideoRepository> logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task AddVideoAsync(string path, string grab)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Путь к видеофайлу не может быть пустым", nameof(path));
            
            if (string.IsNullOrWhiteSpace(grab))
                throw new ArgumentException("Путь к изображению превью не может быть пустым", nameof(grab));

            try
            {
                var videoModel = new VideoModel 
                { 
                    Path = path, 
                    Grab = grab, 
                    Date = DateTime.Now 
                };

                _context.Video.Add(videoModel);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Добавлена новая видеозапись: {VideoPath}", path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при добавлении видеозаписи: {VideoPath}", path);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task<List<VideoModel>> GetVideosAsync(DateTime startDate, DateTime endDate)
        {
            try
            {
                var videos = await _context.Video
                    .AsNoTracking()
                    .Where(v => v.Date >= startDate && v.Date <= endDate)
                    .OrderBy(v => v.Date)
                    .ToListAsync();

                _logger.LogInformation("Получено {Count} видеозаписей за период с {StartDate} по {EndDate}", 
                    videos.Count, startDate, endDate);

                return videos;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении видеозаписей за период с {StartDate} по {EndDate}", 
                    startDate, endDate);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task<VideoModel?> GetLastVideoAsync()
        {
            try
            {
                var lastVideo = await _context.Video
                    .AsNoTracking()
                    .OrderByDescending(v => v.Date)
                    .FirstOrDefaultAsync();

                if (lastVideo != null)
                {
                    _logger.LogInformation("Получена последняя видеозапись: {VideoPath}", lastVideo.Path);
                }
                else
                {
                    _logger.LogInformation("Видеозаписи не найдены");
                }

                return lastVideo;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении последней видеозаписи");
                throw;
            }
        }

        /// <inheritdoc />
        public async Task<bool> RemoveByPathAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Путь к видеофайлу не может быть пустым", nameof(path));

            try
            {
                var video = await _context.Video
                    .FirstOrDefaultAsync(x => x.Path == path);

                if (video == null)
                {
                    _logger.LogWarning("Видеозапись не найдена для удаления: {VideoPath}", path);
                    return false;
                }

                _context.Video.Remove(video);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Удалена видеозапись: {VideoPath}", path);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при удалении видеозаписи: {VideoPath}", path);
                throw;
            }
        }
    }
}