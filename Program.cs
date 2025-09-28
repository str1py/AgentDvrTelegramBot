﻿﻿﻿﻿﻿﻿
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using CountryTelegramBot;
using CountryTelegramBot.Configs;
using CountryTelegramBot.Repositories;
using CountryTelegramBot.Services;

internal class Program
{
    public static async Task Main(string[] args)
    {
<<<<<<< Updated upstream
        try
        {
            var config = BuildConfiguration();
            var services = new ServiceCollection();
            ConfigureServices(services, config);

            var serviceProvider = services.BuildServiceProvider();

            var agent = serviceProvider.GetRequiredService<AgentDVR>();
            await agent.InitializeAsync();
            var telegramBot = serviceProvider.GetRequiredService<TelegramBot>();

=======
        var host = CreateHostBuilder(args).Build();
        
        try
        {
            using var scope = host.Services.CreateScope();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
            
            logger.LogInformation("Запуск AgentDvrTelegramBot...");
            
            // Проверяем подключение к базе данных
            var dbContext = scope.ServiceProvider.GetRequiredService<DbCountryContext>();
            if (await dbContext.Database.CanConnectAsync())
            {
                logger.LogInformation("Подключение к базе данных установлено успешно");
            }
            else
            {
                logger.LogError("Не удалось подключиться к базе данных");
                return;
            }
            
            // Инициализируем AgentDVR
            var agentDvr = scope.ServiceProvider.GetRequiredService<AgentDVR>();
            await agentDvr.InitializeAsync();
            
            // Запускаем VideoWatcher
            var videoWatcher = scope.ServiceProvider.GetRequiredService<VideoWatcher>();
            logger.LogInformation("Мониторинг видеофайлов запущен");
            
            // Запускаем Telegram Bot
            var telegramBot = scope.ServiceProvider.GetRequiredService<TelegramBot>();
>>>>>>> Stashed changes
            await telegramBot.StartBot();
        }
        catch (Exception ex)
        {
<<<<<<< Updated upstream
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
=======
            var logger = host.Services.GetService<ILogger<Program>>();
            logger?.LogError(ex, "Критическая ошибка при запуске приложения");
            throw;
>>>>>>> Stashed changes
        }
    }

    private static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                config.SetBasePath(Directory.GetCurrentDirectory())
                      .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            })
            .ConfigureServices((context, services) =>
            {
                var configuration = context.Configuration;
                
                // Конфигурация настроек
                services.Configure<CommonConfig>(configuration.GetSection("Common"));
                services.Configure<TelegramBotConfig>(configuration.GetSection("TelegramBot"));
                services.Configure<AgentDVRConfig>(configuration.GetSection("AgentDVR"));
                services.Configure<SnapshotWatcherConfig>(configuration.GetSection("SnapshotWatcher"));
                services.Configure<ConnectionStringsConfig>(configuration.GetSection("ConnectionStrings"));
                
                // Получаем строку подключения
                var dbConfig = configuration.GetSection("ConnectionStrings").Get<ConnectionStringsConfig>();
                if (string.IsNullOrWhiteSpace(dbConfig?.DefaultConnection))
                    throw new InvalidOperationException("Строка подключения к базе данных не настроена");
                
                // Настройка Entity Framework
                services.AddDbContext<DbCountryContext>(options =>
                    options.UseMySql(dbConfig.DefaultConnection, ServerVersion.AutoDetect(dbConfig.DefaultConnection)));
                
                // Регистрация сервисов
                services.AddScoped<IVideoRepository, VideoRepository>();
                services.AddSingleton<TimeHelper>();
                services.AddSingleton<FileHelper>();
                
                // Регистрация AgentDVR с фабрикой
                services.AddSingleton<AgentDVR>(provider =>
                {
                    var config = provider.GetRequiredService<IConfiguration>();
                    var logger = provider.GetRequiredService<ILogger<AgentDVR>>();
                    var httpClient = provider.GetRequiredService<HttpClient>();
                    
                    var dvrConfig = config.GetSection("AgentDVR").Get<AgentDVRConfig>();
                    var commonConfig = config.GetSection("Common").Get<CommonConfig>();
                    
                    return new AgentDVR(dvrConfig.Url, dvrConfig.User, dvrConfig.Password, commonConfig, logger, httpClient);
                });
                
                // Регистрация TelegramBot с фабрикой
                services.AddSingleton<TelegramBot>(provider =>
                {
                    var config = provider.GetRequiredService<IConfiguration>();
                    var logger = provider.GetRequiredService<ILogger<TelegramBot>>();
                    var agentDvr = provider.GetRequiredService<AgentDVR>();
                    var fileHelper = provider.GetRequiredService<FileHelper>();
                    
                    var botConfig = config.GetSection("TelegramBot").Get<TelegramBotConfig>();
                    
                    // Создаем скоп для получения videoRepository
                    using var scope = provider.CreateScope();
                    var videoRepository = scope.ServiceProvider.GetRequiredService<IVideoRepository>();
                    
                    return new TelegramBot(botConfig.BotToken, botConfig.ChatId, agentDvr, videoRepository, fileHelper, logger);
                });
                
                // Регистрация интерфейса
                services.AddSingleton<ITelegramBotService>(provider => provider.GetRequiredService<TelegramBot>());
                
                // Регистрация VideoWatcher с фабрикой
                services.AddSingleton<VideoWatcher>(provider =>
                {
                    var config = provider.GetRequiredService<IConfiguration>();
                    var logger = provider.GetRequiredService<ILogger<VideoWatcher>>();
                    var telegramBot = provider.GetRequiredService<ITelegramBotService>();
                    var timeHelper = provider.GetRequiredService<TimeHelper>();
                    var fileHelper = provider.GetRequiredService<FileHelper>();
                    
                    var commonConfig = config.GetSection("Common").Get<CommonConfig>();
                    var watcherConfig = config.GetSection("SnapshotWatcher").Get<SnapshotWatcherConfig>();
                    
                    // Создаем скоп для получения videoRepository
                    using var scope = provider.CreateScope();
                    var videoRepository = scope.ServiceProvider.GetRequiredService<IVideoRepository>();
                    
                    return new VideoWatcher(telegramBot, videoRepository, commonConfig, timeHelper, fileHelper, watcherConfig.Folders, logger);
                });
                
                // Настройка HttpClient
                services.AddHttpClient();
            });

    private static IConfigurationRoot BuildConfiguration()
    {
        return new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile($"{Directory.GetCurrentDirectory()}\\appsettings.json", optional: false, reloadOnChange: true)
            .Build();
    }
}



