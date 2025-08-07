
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using CountryTelegramBot;
using CountryTelegramBot.Configs;

internal class Program
{
    public static async Task Main(string[] args)
    {
        var config = BuildConfiguration();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        ILogger logger = loggerFactory.CreateLogger("CountryTelegramBot");

        // Получение конфигов через POCO
        var commonConf = config.GetSection("Common").Get<CommonConfig>() ?? new CommonConfig();
        var botConfig = config.GetSection("TelegramBot").Get<TelegramBotConfig>() ?? throw new Exception("Отсутствует секция TelegramBot в конфиге");
        var dvrConfig = config.GetSection("AgentDVR").Get<AgentDVRConfig>() ?? throw new Exception("Отсутствует секция AgentDVR в конфиге");
        var watcherConfig = config.GetSection("SnapshotWatcher").Get<SnapshotWatcherConfig>() ?? new SnapshotWatcherConfig();
        var dbConfig = config.GetSection("ConnectionStrings").Get<ConnectionStringsConfig>() ?? throw new Exception("Отсутствует секция ConnectionStrings в конфиге");

        // Проверки обязательных параметров
        if (string.IsNullOrWhiteSpace(botConfig.BotToken))
            throw new Exception("BotToken не задан в конфиге");
        if (string.IsNullOrWhiteSpace(botConfig.ChatId))
            logger.LogWarning("ChatId не задан в конфиге");
        if (string.IsNullOrWhiteSpace(dvrConfig.Url) || string.IsNullOrWhiteSpace(dvrConfig.User) || string.IsNullOrWhiteSpace(dvrConfig.Password))
            logger.LogWarning("AgentDVR параметры не заданы полностью");
        if (string.IsNullOrWhiteSpace(dbConfig.DefaultConnection))
            throw new Exception("Строка подключения к БД не задана");

        // Инициализация зависимостей
        var timeHelper = new TimeHelper(logger);
        var agent = new AgentDVR(dvrConfig.Url, dvrConfig.User, dvrConfig.Password, commonConf, logger);
        await agent.InitializeAsync();
        var options = new DbContextOptionsBuilder<DbCountryContext>().UseMySql(dbConfig.DefaultConnection, ServerVersion.AutoDetect(dbConfig.DefaultConnection), null).Options;
        var dbConnection = new DbConnection(logger, options);
        var telegramBot = new TelegramBot(botConfig.BotToken, botConfig.ChatId, agent, dbConnection, logger);
        var videoWatcher = new VideoWatcher(telegramBot, dbConnection, commonConf, timeHelper, watcherConfig.Folders, logger);

        await telegramBot.StartBot();
    }

    private static IConfigurationRoot BuildConfiguration()
    {
        return new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile($"{Directory.GetCurrentDirectory()}\\appsettings.json", optional: false, reloadOnChange: true)
            .Build();
    }
}



