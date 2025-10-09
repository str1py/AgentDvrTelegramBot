using Microsoft.Extensions.Logging;
using CountryTelegramBot.Models;
using CountryTelegramBot.Repositories;
using System;
using System.Threading.Tasks;

namespace CountryTelegramBot.Services
{
    public class TestReportService : ITestReportService
    {
        private readonly ITelegramBotService _telegramBotService;
        private readonly IVideoRepository _videoRepository;
        private readonly TimeHelper _timeHelper;
        private readonly IReportService _reportService;
        private readonly ILogger<TestReportService> _logger;
        private readonly CountryTelegramBot.Configs.CommonConfig _commonConfig;

        public TestReportService(
            ITelegramBotService telegramBotService,
            IVideoRepository videoRepository,
            TimeHelper timeHelper,
            IReportService reportService,
            CountryTelegramBot.Configs.CommonConfig commonConfig,
            ILogger<TestReportService> logger)
        {
            _telegramBotService = telegramBotService ?? throw new ArgumentNullException(nameof(telegramBotService));
            _videoRepository = videoRepository ?? throw new ArgumentNullException(nameof(videoRepository));
            _timeHelper = timeHelper ?? throw new ArgumentNullException(nameof(timeHelper));
            _reportService = reportService ?? throw new ArgumentNullException(nameof(reportService));
            _commonConfig = commonConfig ?? throw new ArgumentNullException(nameof(commonConfig));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Отправляет тестовый отчет
        /// </summary>
        public async Task SendTestReportAsync()
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

        /// <summary>
        /// Проверяет и отправляет отчеты за сегодня, если они еще не были отправлены
        /// </summary>
        public async Task CheckAndSendTodaysReportsAsync()
        {
            try
            {
                // Получаем тип наблюдателя из конфигурации
                var reportType = _reportService.GetWatcherType(_commonConfig.WatcherType ?? "ASAP");
                
                // Проверяем и отправляем отчеты за сегодня ТОЛЬКО если это необходимо
                // В режиме Morning и MorningAndEvening отчеты отправляются по расписанию, не при запуске
                if (reportType == CountryTelegramBot.Services.WatcherType.ASAP)
                {
                    _logger.LogInformation("Проверка и отправка отчетов за сегодня для режима ASAP");
                    await _reportService.CheckAndSendTodaysReports(reportType);
                }
                else
                {
                    _logger.LogInformation($"Для типа наблюдателя {reportType} отчеты отправляются по расписанию, проверка при запуске не требуется");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ошибка при проверке и отправке отчетов за сегодня. Продолжаем работу.");
            }
        }
    }
}