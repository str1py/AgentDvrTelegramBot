using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using CountryTelegramBot;
using System.Dynamic;

internal class Program
{
    //C:\Users\rdp.wonderland-ch\Desktop\new\CountryTelegramBot
    public static async Task Main(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile($"{Directory.GetCurrentDirectory()}\\appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
        });
        ILogger logger = loggerFactory.CreateLogger("CountryTelegramBot");
        var commonConf = config.GetSection("Common");
        var botConfig = config.GetSection("TelegramBot");
        var dvrConfig = config.GetSection("AgentDVR");
        var watcherConfig = config.GetSection("SnapshotWatcher");
        var dbConfig = config.GetSection("ConnectionStrings");

        var botToken = botConfig["BotToken"] ?? string.Empty;
        var chatId = botConfig["ChatId"] ?? string.Empty;

        var dvrUrl = dvrConfig["Url"] ?? string.Empty;
        var dvrUser = dvrConfig["User"] ?? string.Empty;
        var dvrPassword = dvrConfig["Password"] ?? string.Empty;

        var folders = watcherConfig.GetSection("Folders").Get<string[]>();

        var dbserver = dbConfig["DefaultConnection"] ?? string.Empty;

        var timeHelper = new TimeHelper(logger);
 

        var agent = new AgentDVR(dvrUrl, dvrUser, dvrPassword, commonConf, logger);

        var options = new DbContextOptionsBuilder<DbCountryContext>().UseMySql((dbserver),ServerVersion.AutoDetect(dbserver), null).Options;
        var dbConnection = new DbConnection(logger,options);

        var telegramBot = new TelegramBot(botToken, chatId, agent, dbConnection, logger);

        var videoWatcher = new VideoWatcher(telegramBot, dbConnection, commonConf, timeHelper, folders, logger);


        await telegramBot.StartBot();
    }
}



