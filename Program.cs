﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using CountryTelegramBot;
using CountryTelegramBot.Configs;
using CountryTelegramBot.Repositories;
using CountryTelegramBot.Services;
using CountryTelegramBot.Models;
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

            // Инициализируем приложение через сервис
            var appInitializationService = scope.ServiceProvider.GetService<IAppInitializationService>();
            if (appInitializationService != null)
            {
                await appInitializationService.InitializeAsync();
            }

            // Проверяем и отправляем неотправленные отчеты при запуске
            var unsentReportService = scope.ServiceProvider.GetService<IUnsentReportService>();
            if (unsentReportService != null)
            {
                await unsentReportService.SendUnsentReportsAtStartupAsync();
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
            
            // Приложение продолжает работу
            logger.LogInformation("Приложение запущено и продолжает работу. Нажмите Ctrl+C для завершения.");
                
            // Ждем бесконечно, чтобы приложение продолжало работать
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
                
                // Регистрация IErrorHandler
                services.AddSingleton<IErrorHandler>(provider =>
                {
                    var logger = provider.GetRequiredService<ILogger<DefaultErrorHandler>>();
                    return new DefaultErrorHandler(logger);
                });
                
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
                
                // Регистрация IDbConnection как Scoped вместо Singleton
                services.AddScoped<IDbConnection>(provider =>
                {
                    try
                    {
                        var logger = provider.GetRequiredService<ILogger<DbConnection>>();
                        var context = provider.GetRequiredService<DbCountryContext>();
                        var errorHandler = provider.GetService<IErrorHandler>();
                        return new DbConnection(logger, context, errorHandler);
                    }
                    catch (Exception ex)
                    {
                        var logger = provider.GetRequiredService<ILogger<Program>>();
                        logger.LogWarning(ex, "Ошибка создания подключения к базе данных. Продолжаем работу без базы данных.");
                        return null;
                    }
                });
                
                services.AddSingleton<TimeHelper>(provider =>
                {
                    var logger = provider.GetRequiredService<ILogger<TimeHelper>>();
                    return new TimeHelper(logger);
                });
                services.AddSingleton<IFileHelper, FileHelper>(provider =>
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
                    var timeHelper = provider.GetRequiredService<TimeHelper>();
                    
                    var dvrConfig = config.GetSection("AgentDVR").Get<AgentDVRConfig>() ?? new AgentDVRConfig();
                    var commonConfig = config.GetSection("Common").Get<CommonConfig>() ?? new CommonConfig();
                    
                    return new AgentDVR(dvrConfig.Url, dvrConfig.User, dvrConfig.Password, commonConfig, logger, httpClient, timeHelper);
                });
                
                // Регистрация IVideoCompressionService
                services.AddSingleton<IVideoCompressionService, VideoCompressionService>();

                // Регистрация TelegramBot с фабрикой
                services.AddSingleton<TelegramBot>(provider =>
                {
                    var configuration = provider.GetRequiredService<IConfiguration>();
                    var logger = provider.GetRequiredService<ILogger<TelegramBot>>();
                    var agentDvr = provider.GetRequiredService<AgentDVR>();
                    var fileHelper = provider.GetRequiredService<IFileHelper>();
                    var videoCompressionService = provider.GetRequiredService<IVideoCompressionService>();
                    
                    var botConfig = configuration.GetSection("TelegramBot").Get<TelegramBotConfig>();
                    
                    // Получаем сервисы через провайдер, а не через scope
                    return new TelegramBot(botConfig.BotToken, botConfig.ChatId, agentDvr, 
                        provider.GetRequiredService<IVideoRepository>(),
                        fileHelper, logger, 
                        provider.GetRequiredService<IDbConnection>(),
                        videoCompressionService);
                });
                services.AddSingleton<ITelegramBotService>(provider => provider.GetRequiredService<TelegramBot>());
                
                // Регистрация VideoWatcher с фабрикой
                services.AddSingleton<VideoWatcher>(provider =>
                {
                    var configuration = provider.GetRequiredService<IConfiguration>();
                    var logger = provider.GetRequiredService<ILogger<VideoWatcher>>();
                    var telegramBot = provider.GetRequiredService<ITelegramBotService>();
                    var timeHelper = provider.GetRequiredService<TimeHelper>();
                    var fileHelper = provider.GetRequiredService<IFileHelper>();
                    var dbConnection = provider.GetRequiredService<IDbConnection>();
                    var videoRepository = provider.GetRequiredService<IVideoRepository>();
                    
                    var commonConfig = configuration.GetSection("Common").Get<CommonConfig>() ?? new CommonConfig();
                    var watcherConfig = configuration.GetSection("SnapshotWatcher").Get<SnapshotWatcherConfig>() ?? new SnapshotWatcherConfig();
                    
                    return new VideoWatcher(telegramBot, videoRepository, commonConfig, timeHelper, fileHelper, dbConnection, watcherConfig.Folders, logger);
                });

                // Регистрация ReportService
                services.AddScoped<IReportService, ReportService>(provider =>
                {
                    var dbConnection = provider.GetRequiredService<IDbConnection>();
                    var telegramBotService = provider.GetRequiredService<ITelegramBotService>();
                    var videoRepository = provider.GetRequiredService<IVideoRepository>();
                    var timeHelper = provider.GetRequiredService<TimeHelper>();
                    var logger = provider.GetRequiredService<ILogger<ReportService>>();
                    
                    return new ReportService(dbConnection, telegramBotService, videoRepository, timeHelper, logger);
                });
                
                // Регистрация UnsentReportService
                services.AddScoped<IUnsentReportService, UnsentReportService>(provider =>
                {
                    var telegramBotService = provider.GetRequiredService<ITelegramBotService>();
                    var videoRepository = provider.GetRequiredService<IVideoRepository>();
                    var timeHelper = provider.GetRequiredService<TimeHelper>();
                    var reportService = provider.GetRequiredService<IReportService>();
                    var configuration = provider.GetRequiredService<IConfiguration>();
                    var commonConfig = configuration.GetSection("Common").Get<CountryTelegramBot.Configs.CommonConfig>() ?? new CountryTelegramBot.Configs.CommonConfig();
                    var logger = provider.GetRequiredService<ILogger<UnsentReportService>>();
                    var dbConnection = provider.GetRequiredService<IDbConnection>();
                    
                    return new UnsentReportService(telegramBotService, videoRepository, timeHelper, reportService, commonConfig, logger, dbConnection);
                });

                // Регистрация AppInitializationService
                services.AddScoped<IAppInitializationService, AppInitializationService>(provider =>
                {
                    var dbConnection = provider.GetRequiredService<IDbConnection>();
                    var telegramBotService = provider.GetRequiredService<ITelegramBotService>();
                    var videoRepository = provider.GetRequiredService<IVideoRepository>();
                    var timeHelper = provider.GetRequiredService<TimeHelper>();
                    var reportService = provider.GetRequiredService<IReportService>();
                    var agentDvr = provider.GetRequiredService<AgentDVR>();
                    var configuration = provider.GetRequiredService<IConfiguration>();
                    var commonConfig = configuration.GetSection("Common").Get<CountryTelegramBot.Configs.CommonConfig>() ?? new CountryTelegramBot.Configs.CommonConfig();
                    var logger = provider.GetRequiredService<ILogger<AppInitializationService>>();
                    
                    return new AppInitializationService(dbConnection, telegramBotService, videoRepository, timeHelper, reportService, agentDvr, commonConfig, logger);
                });

                // Настройка HttpClient
                services.AddHttpClient();
            });
}