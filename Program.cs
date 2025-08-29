
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using CountryTelegramBot;
using CountryTelegramBot.Configs;

internal class Program
{
    public static async Task Main(string[] args)
    {
        try
        {
            var config = BuildConfiguration();
            var services = new ServiceCollection();
            ConfigureServices(services, config);

            var serviceProvider = services.BuildServiceProvider();

            var agent = serviceProvider.GetRequiredService<AgentDVR>();
            await agent.InitializeAsync();
            var telegramBot = serviceProvider.GetRequiredService<TelegramBot>();

            await telegramBot.StartBot();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FATAL] {ex.Message}\n{ex.StackTrace}");
            // Можно добавить логирование в файл или отправку уведомления админу
        }
    }

    // Вынос регистрации сервисов и конфигов в отдельный метод
    private static void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton(config);
        services.AddLogging(builder => builder.AddConsole());

        var commonConf = config.GetSection("Common").Get<CommonConfig>() ?? new CommonConfig();
    var botConfig = config.GetSection("TelegramBot").Get<TelegramBotConfig>() ?? throw new Exception("Отсутствует секция TelegramBot в конфиге");
    var dvrConfig = config.GetSection("AgentDVR").Get<AgentDVRConfig>() ?? throw new Exception("Отсутствует секция AgentDVR в конфиге");

    // Безопасное получение токенов и паролей из переменных окружения
    var envBotToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
    var envChatId = Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID");
    var envDvrUrl = Environment.GetEnvironmentVariable("AGENTDVR_URL");
    var envDvrUser = Environment.GetEnvironmentVariable("AGENTDVR_USER");
    var envDvrPassword = Environment.GetEnvironmentVariable("AGENTDVR_PASSWORD");

    if (!string.IsNullOrWhiteSpace(envBotToken)) botConfig.BotToken = envBotToken;
    if (!string.IsNullOrWhiteSpace(envChatId)) botConfig.ChatId = envChatId;
    if (!string.IsNullOrWhiteSpace(envDvrUrl)) dvrConfig.Url = envDvrUrl;
    if (!string.IsNullOrWhiteSpace(envDvrUser)) dvrConfig.User = envDvrUser;
    if (!string.IsNullOrWhiteSpace(envDvrPassword)) dvrConfig.Password = envDvrPassword;
        var watcherConfig = config.GetSection("SnapshotWatcher").Get<SnapshotWatcherConfig>() ?? new SnapshotWatcherConfig();
        var dbConfig = config.GetSection("ConnectionStrings").Get<ConnectionStringsConfig>() ?? throw new Exception("Отсутствует секция ConnectionStrings в конфиге");

        ValidateConfig(commonConf, "CommonConfig");
        ValidateConfig(botConfig, "TelegramBotConfig");
        ValidateConfig(dvrConfig, "AgentDVRConfig");
        ValidateConfig(watcherConfig, "SnapshotWatcherConfig");
        ValidateConfig(dbConfig, "ConnectionStringsConfig");

        services.AddSingleton(commonConf);
        services.AddSingleton(botConfig);
        services.AddSingleton(dvrConfig);
        services.AddSingleton(watcherConfig);
        services.AddSingleton(dbConfig);
        services.AddSingleton<TimeHelper>();
        services.AddSingleton<AgentDVR>(sp =>
            new AgentDVR(dvrConfig.Url, dvrConfig.User, dvrConfig.Password, commonConf, sp.GetRequiredService<ILogger<AgentDVR>>())
        );
        services.AddSingleton<DbConnection>(sp =>
        {
            var options = new DbContextOptionsBuilder<DbCountryContext>().UseMySql(dbConfig.DefaultConnection, ServerVersion.AutoDetect(dbConfig.DefaultConnection), null).Options;
            return new DbConnection(sp.GetRequiredService<ILogger<DbConnection>>(), options);
        });
        services.AddSingleton<TelegramBot>(sp =>
            new TelegramBot(botConfig.BotToken, botConfig.ChatId, sp.GetRequiredService<AgentDVR>(), sp.GetRequiredService<DbConnection>(), sp.GetRequiredService<ILogger<TelegramBot>>())
        );
        services.AddSingleton<VideoWatcher>(sp =>
            new VideoWatcher(sp.GetRequiredService<TelegramBot>(), sp.GetRequiredService<DbConnection>(), commonConf, sp.GetRequiredService<TimeHelper>(), watcherConfig.Folders, sp.GetRequiredService<ILogger<VideoWatcher>>())
        );
    }

    // Метод для валидации конфигов через DataAnnotations
    private static void ValidateConfig(object config, string configName)
    {
        var context = new System.ComponentModel.DataAnnotations.ValidationContext(config, null, null);
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        if (!System.ComponentModel.DataAnnotations.Validator.TryValidateObject(config, context, results, true))
        {
            var errors = string.Join("; ", results.Select(r => r.ErrorMessage));
            throw new Exception($"Ошибка валидации {configName}: {errors}");
        }
    }

    private static IConfigurationRoot BuildConfiguration()
    {
        return new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile($"{Directory.GetCurrentDirectory()}\\appsettings.json", optional: false, reloadOnChange: true)
            .Build();
    }
}



