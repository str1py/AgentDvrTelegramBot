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

            // Запускаем периодическую проверку неотправленных отчетов
            _reportService.StartPeriodicCheck();

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
    }
}