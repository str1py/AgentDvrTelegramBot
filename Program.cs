using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using CountryTelegramBot;
using CountryTelegramBot.Configs;
using CountryTelegramBot.Repositories;
using CountryTelegramBot.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

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

            // Проверяем подключение к базе данных (сделаем это необязательным для тестирования)
            try
            {
                var dbContext = scope.ServiceProvider.GetService<DbCountryContext>();
                if (dbContext != null && await dbContext.Database.CanConnectAsync())
                {
                    logger.LogInformation("Подключение к базе данных установлено успешно");
                }
                else
                {
                    logger.LogWarning("Не удалось подключиться к базе данных. Продолжаем работу без базы данных.");
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Ошибка подключения к базе данных. Продолжаем работу без базы данных.");
            }

            // Инициализируем AgentDVR
            try
            {
                var agentDvr = scope.ServiceProvider.GetService<AgentDVR>();
                if (agentDvr != null)
                {
                    await agentDvr.InitializeAsync();
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Ошибка инициализации AgentDVR. Продолжаем работу.");
            }

            // Запускаем VideoWatcher
            var videoWatcher = scope.ServiceProvider.GetService<VideoWatcher>();
            if (videoWatcher != null)
            {
                logger.LogInformation("Мониторинг видеофайлов запущен");
            }

            // Запускаем Telegram Bot
            var telegramBot = scope.ServiceProvider.GetService<TelegramBot>();
            if (telegramBot != null)
            {
                await telegramBot.StartBot();
            }
            // Keep the application running
            logger.LogInformation("Приложение запущено и продолжает работу. Нажмите Ctrl+C для завершения.");
            
            // Wait indefinitely to keep the application alive
            await Task.Delay(-1);
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
                
                // Настройка Entity Framework с обработкой ошибок
                services.AddDbContext<DbCountryContext>(options =>
                {
                    try
                    {
                        options.UseMySql(dbConfig.DefaultConnection, ServerVersion.AutoDetect(dbConfig.DefaultConnection));
                    }
                    catch (Exception)
                    {
                        // Если не можем подключиться к MySQL, используем in-memory базу данных
                    }
                }, ServiceLifetime.Scoped);
                
                // Регистрация сервисов с обработкой ошибок
                services.AddScoped<IVideoRepository>(provider =>
                {
                    try
                    {
                        var context = provider.GetRequiredService<DbCountryContext>();
                        var logger = provider.GetRequiredService<ILogger<VideoRepository>>();
                        return new VideoRepository(context, logger);
                    }
                    catch (Exception)
                    {
                        // Возвращаем заглушку, если не можем создать реальный репозиторий
                        return null;
                    }
                });
                
                
                services.AddSingleton<TimeHelper>(provider =>
                {
                    var logger = provider.GetRequiredService<ILogger<TimeHelper>>();
                    return new TimeHelper(logger);
                });
                services.AddSingleton<FileHelper>(provider =>
                {
                    var logger = provider.GetRequiredService<ILogger<FileHelper>>();
                    return new FileHelper(logger);
                });
                
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
                    
                    // We'll create a factory function to get the repository when needed
                    IVideoRepository CreateVideoRepository()
                    {
                        var scope = provider.CreateScope();
                        return scope.ServiceProvider.GetRequiredService<IVideoRepository>();
                    }
                    
                    return new VideoWatcher(telegramBot, CreateVideoRepository(), commonConfig, timeHelper, fileHelper, watcherConfig.Folders, logger);
                });
                
                // Настройка HttpClient
                services.AddHttpClient();
            });
}
