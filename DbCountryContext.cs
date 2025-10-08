using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using System.Data.Common;
using MySqlConnector;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CountryTelegramBot
{
    public class DbCountryContext: DbContext
    {
        public DbCountryContext(DbContextOptions<DbCountryContext> options)
                : base(options) { }
        
        public DbSet<VideoModel> Video { get; set; }
        public DbSet<Models.ReportStatusModel> ReportStatus { get; set; }
        
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                // This is a fallback configuration, normally configured through DI
                var configuration = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json")
                    .AddJsonFile("appsettings.Development.json", optional: true)
                    .Build();
                    
                var connectionString = configuration.GetSection("ConnectionStrings:DefaultConnection").Value;
                if (!string.IsNullOrEmpty(connectionString))
                {
                    optionsBuilder.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
                }
            }
        }
        

    }
}