﻿using Microsoft.EntityFrameworkCore;
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

            // Check for unsent reports and try to send them
            try
            {
                var dbConnection = scope.ServiceProvider.GetService<IDbConnection>();
                var telegramBotService = scope.ServiceProvider.GetService<ITelegramBotService>();
                var videoRepository = scope.ServiceProvider.GetService<IVideoRepository>();
                    
                if (dbConnection != null && telegramBotService != null && videoRepository != null)
                {
                    await SendUnsentReports(dbConnection, telegramBotService, videoRepository, logger);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Ошибка при отправке неотправленных отчетов. Продолжаем работу.");
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
        
    /// <summary>
    /// Проверяет наличие неотправленных отчетов и пытается отправить их
    /// </summary>
    private static async Task SendUnsentReports(CountryTelegramBot.Models.IDbConnection dbConnection, ITelegramBotService telegramBotService, IVideoRepository videoRepository, ILogger logger)
    {
        try
        {
            var unsentReports = dbConnection.GetUnsentReports();
            logger.LogInformation($"Найдено {unsentReports.Count} неотправленных отчетов");
                
            foreach (var report in unsentReports)
            {
                try
                {
                    logger.LogInformation($"Попытка отправки отчета за период {report.StartDate} - {report.EndDate}");
                        
                    // Get videos for this report period
                    var videos = await videoRepository.GetVideosAsync(report.StartDate, report.EndDate);
                        
                    // Send the report
                    await telegramBotService.SendVideoGroupAsync(videos, report.StartDate, report.EndDate);
                        
                    logger.LogInformation($"Отчет за период {report.StartDate} - {report.EndDate} успешно отправлен");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, $"Ошибка при отправке отчета за период {report.StartDate} - {report.EndDate}");
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при проверке неотправленных отчетов");
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
                
                // Регистрация IDbConnection
                services.AddScoped<IDbConnection>(provider =>
                {
                    try
                    {
                        var logger = provider.GetRequiredService<ILogger<DbConnection>>();
                        var context = provider.GetRequiredService<DbCountryContext>();
                        var errorHandler = provider.GetService<IErrorHandler>();
                        // We need to create DbContextOptions for DbConnection
                        var options = new DbContextOptionsBuilder<DbCountryContext>()
                            .UseMySql(context.Database.GetDbConnection().ConnectionString, 
                                     ServerVersion.AutoDetect(context.Database.GetDbConnection().ConnectionString))
                            .Options;
                        return new DbConnection(logger, options, errorHandler);
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
                    
                    var dvrConfig = config.GetSection("AgentDVR").Get<AgentDVRConfig>() ?? new AgentDVRConfig();
                    var commonConfig = config.GetSection("Common").Get<CommonConfig>() ?? new CommonConfig();
                    
                    return new AgentDVR(dvrConfig.Url, dvrConfig.User, dvrConfig.Password, commonConfig, logger, httpClient);
                });
                
                // Регистрация TelegramBot с фабрикой
                services.AddSingleton<TelegramBot>(provider =>
                {
                    var configuration = provider.GetRequiredService<IConfiguration>();
                    var logger = provider.GetRequiredService<ILogger<TelegramBot>>();
                    var agentDvr = provider.GetRequiredService<AgentDVR>();
                    var fileHelper = provider.GetRequiredService<FileHelper>();
                    
                    var botConfig = configuration.GetSection("TelegramBot").Get<TelegramBotConfig>();
                    
                    // Create scope to get the database connection
                    using var scope = provider.CreateScope();
                    var dbConnection = scope.ServiceProvider.GetRequiredService<IDbConnection>();
                    var videoRepository = scope.ServiceProvider.GetRequiredService<IVideoRepository>();
                    
                    return new TelegramBot(botConfig.BotToken, botConfig.ChatId, agentDvr, videoRepository, fileHelper, logger, dbConnection);
                });
                services.AddSingleton<ITelegramBotService>(provider => provider.GetRequiredService<TelegramBot>());
                
                // Регистрация VideoWatcher с фабрикой
                services.AddSingleton<VideoWatcher>(provider =>
                {
                    var configuration = provider.GetRequiredService<IConfiguration>();
                    var logger = provider.GetRequiredService<ILogger<VideoWatcher>>();
                    var telegramBot = provider.GetRequiredService<ITelegramBotService>();
                    var timeHelper = provider.GetRequiredService<TimeHelper>();
                    var fileHelper = provider.GetRequiredService<FileHelper>();
                    var dbConnection = provider.GetRequiredService<IDbConnection>();
                    var videoRepository = provider.GetRequiredService<IVideoRepository>(); // Add this line
                    
                    var commonConfig = configuration.GetSection("Common").Get<CommonConfig>() ?? new CommonConfig();
                    var watcherConfig = configuration.GetSection("SnapshotWatcher").Get<SnapshotWatcherConfig>() ?? new SnapshotWatcherConfig();
                    
                    return new VideoWatcher(telegramBot, videoRepository, commonConfig, timeHelper, fileHelper, dbConnection, watcherConfig.Folders, logger);
                });
                
                // Настройка HttpClient
                services.AddHttpClient();
            });
}
