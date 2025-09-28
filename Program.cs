﻿﻿﻿using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using CountryTelegramBot;
using CountryTelegramBot.Configs;
using CountryTelegramBot.Repositories;
using CountryTelegramBot.Services;

internal class Program
{
    public static async Task Main(string[] args)
    {
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
            await telegramBot.StartBot();
        }
        catch (Exception ex)
        {
            var logger = host.Services.GetService<ILogger<Program>>();
            logger?.LogError(ex, "Критическая ошибка при запуске приложения");
            throw;
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
}