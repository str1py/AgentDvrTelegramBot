using Microsoft.Extensions.Logging;
using CountryTelegramBot.Models;
using CountryTelegramBot.Repositories;
using CountryTelegramBot.Services;
using System;
using System.Threading.Tasks;

namespace CountryTelegramBot.Services
{
    public class AppInitializationService : IAppInitializationService
    {
        private readonly IDbConnection _dbConnection;
        private readonly ITelegramBotService _telegramBotService;
        private readonly IVideoRepository _videoRepository;
        private readonly TimeHelper _timeHelper;
        private readonly IReportService _reportService;
        private readonly AgentDVR _agentDvr;
        private readonly ILogger<AppInitializationService> _logger;
        private readonly CountryTelegramBot.Configs.CommonConfig _commonConfig;

        public AppInitializationService(
            IDbConnection dbConnection,
            ITelegramBotService telegramBotService,
            IVideoRepository videoRepository,
            TimeHelper timeHelper,
            IReportService reportService,
            AgentDVR agentDvr,
            CountryTelegramBot.Configs.CommonConfig commonConfig,
            ILogger<AppInitializationService> logger)
        {
            _dbConnection = dbConnection ?? throw new ArgumentNullException(nameof(dbConnection));
            _telegramBotService = telegramBotService ?? throw new ArgumentNullException(nameof(telegramBotService));
            _videoRepository = videoRepository ?? throw new ArgumentNullException(nameof(videoRepository));
            _timeHelper = timeHelper ?? throw new ArgumentNullException(nameof(timeHelper));
            _reportService = reportService ?? throw new ArgumentNullException(nameof(reportService));
            _agentDvr = agentDvr ?? throw new ArgumentNullException(nameof(agentDvr));
            _commonConfig = commonConfig ?? throw new ArgumentNullException(nameof(commonConfig));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task InitializeAsync()
        {
            _logger.LogInformation("Инициализация приложения...");

            // Проверяем подключение к базе данных
            await CheckDatabaseConnection();

            // Инициализируем AgentDVR
            await InitializeAgentDvr();

            // Отправляем тестовый отчет (если нужно)
           // await SendTestReport();

            // Проверяем и отправляем отчеты за сегодня
            await CheckAndSendTodaysReports();

            _logger.LogInformation("Инициализация приложения завершена");
        }

        private async Task CheckDatabaseConnection()
        {
            try
            {
                // Проверка подключения к базе данных уже выполняется в DbConnection
                if (_dbConnection.IsConnected)
                {
                    _logger.LogInformation("Подключение к базе данных установлено успешно");
                }
                else
                {
                    _logger.LogWarning("Не удалось подключиться к базе данных. Продолжаем работу без базы данных.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ошибка подключения к базе данных. Продолжаем работу без базы данных.");
            }
        }

        private async Task InitializeAgentDvr()
        {
            try
            {
                await _agentDvr.InitializeAsync();
                _logger.LogInformation("AgentDVR инициализирован успешно");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ошибка инициализации AgentDVR. Продолжаем работу.");
            }
        }

        private async Task SendTestReport()
        {
            try
            {
                _logger.LogInformation("ТЕСТ: Отправка последнего отчета");
                
                // Определяем период для последнего отчета (прошлая ночь)
                var startDate = _timeHelper.NightVideoStartDate.AddDays(-1);
                var endDate = _timeHelper.NightVideoEndDate.AddDays(-1);
                
                _logger.LogInformation($"ТЕСТ: Получение видео за период {startDate} - {endDate}");
                
                // Получаем видео за последний период
                var videos = await _videoRepository.GetVideosAsync(startDate, endDate);
                
                if (videos.Count > 0)
                {
                    _logger.LogInformation($"ТЕСТ: Найдено {videos.Count} видео. Отправка отчета...");
                    
                    // Отправляем отчет
                    await _telegramBotService.SendVideoGroupAsync(videos, startDate, endDate);
                    
                    _logger.LogInformation("ТЕСТ: Последний отчет успешно отправлен");
                }
                else
                {
                    _logger.LogInformation("ТЕСТ: Нет видео для отправки в последнем отчете");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ТЕСТ: Ошибка при отправке последнего отчета");
            }
        }

        private async Task CheckAndSendTodaysReports()
        {
            try
            {
                // Получаем тип наблюдателя из конфигурации
                var watcherType = GetWatcherType(_commonConfig.WatcherType ?? "ASAP");
                
                await _reportService.CheckAndSendTodaysReports(watcherType);
                _reportService.StartPeriodicCheck();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ошибка при проверке и отправке отчетов за сегодня. Продолжаем работу.");
            }
        }

        private CountryTelegramBot.Services.WatcherType GetWatcherType(string type)
        {
            if (type == "ASAP")
            {
                return CountryTelegramBot.Services.WatcherType.ASAP;
            }
            else if (type == "Morning")
            {
                return CountryTelegramBot.Services.WatcherType.Morning;
            }
            else
            {
                return CountryTelegramBot.Services.WatcherType.MorningAndEvening;
            }
        }
    }
}