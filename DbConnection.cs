using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;

namespace CountryTelegramBot
{
    public class DbConnection : DbContext
    {
        public bool isConnected = false;
        public DbCountryContext dbCountryContext;
        private readonly ILogger? logger;

        public DbConnection(ILogger? logger, DbContextOptions<DbCountryContext> options)
        {
            this.logger = logger;

            try
            {
                dbCountryContext = new DbCountryContext(options);
                isConnected = dbCountryContext.Database.CanConnect();
                if (isConnected)
                    logger?.LogInformation($"Connected to database seccussesfully!");
                else
                    logger?.LogError($"Connection error");
            }
            catch (Exception ex)
            {
                logger?.LogError($"Connection error: {ex.Message}");
            }
        }

        public async Task AddVideoData(string path, string grab)
        {
            dbCountryContext.Video
                .Add(new VideoModel { Path = path, Grab = grab, Date = DateTime.Now });
            await dbCountryContext.SaveChangesAsync();
        }

        public List<VideoModel> GetVideos(DateTime startDate, DateTime endDate)
        {
            return dbCountryContext.Video
                .AsNoTracking()
                .Where(v => v.Date >= startDate && v.Date <= endDate)
                .ToList();
        }
        public async Task<VideoModel> GetLastVideo()
        {
            return await dbCountryContext.Video.AsNoTracking().OrderByDescending(v => v.Date).LastOrDefaultAsync();
        }

        public async Task<bool> RemoveItemByPath(string path)
        {
            var item = await dbCountryContext.Video.FirstOrDefaultAsync(x => x.Path == path);
            if (item != null)
            {
                dbCountryContext.Remove(item);
                await dbCountryContext.SaveChangesAsync();
                return true;
            }
            return false;
    
        }
   
    }
}
