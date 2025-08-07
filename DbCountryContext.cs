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

namespace CountryTelegramBot
{
    public class DbCountryContext: DbContext
    {
        public DbCountryContext(DbContextOptions<DbCountryContext> options)
                : base(options) { }
        public DbSet<VideoModel> Video { get; set; }

    }
}
