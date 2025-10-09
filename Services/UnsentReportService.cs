using Microsoft.Extensions.Logging;
using CountryTelegramBot.Models;
using CountryTelegramBot.Repositories;
using CountryTelegramBot.Services;
using CountryTelegramBot.Configs;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CountryTelegramBot.Services
{
    public class UnsentReportService : IUnsentReportService
    {
        private readonly ITelegramBotService _telegramBotService;
        private readonly IVideoRepository _videoRepository;
        private readonly TimeHelper _timeHelper;
        private readonly IReportService _reportService;
        private readonly CommonConfig _commonConfig;
        private readonly ILogger<UnsentReportService> _logger;
        private readonly IDbConnection _dbConnection;

        public UnsentReportService(
            ITelegramBotService telegramBotService,
            IVideoRepository videoRepository,
            TimeHelper timeHelper,
            IReportService reportService,
            CommonConfig commonConfig,
            ILogger<UnsentReportService> logger,
            IDbConnection dbConnection)
        {
            _telegramBotService = telegramBotService ?? throw new ArgumentNullException(nameof(telegramBotService));
            _videoRepository = videoRepository ?? throw new ArgumentNullException(nameof(videoRepository));
            _timeHelper = timeHelper ?? throw new ArgumentNullException(nameof(timeHelper));
            _reportService = reportService ?? throw new ArgumentNullException(nameof(reportService));
            _commonConfig = commonConfig ?? throw new ArgumentNullException(nameof(commonConfig));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _dbConnection = dbConnection ?? throw new ArgumentNullException(nameof(dbConnection));
        }

        /// <summary>
        /// Проверяет и отправляет неотправленные отчеты при запуске приложения
        /// </summary>
        public async Task SendUnsentReportsAtStartupAsync()
        {
            try
            {
                _logger.LogInformation("Проверка неотправленных отчетов при запуске приложения");
                
                // Получаем все неотправленные отчеты из базы данных
                var unsentReports = _dbConnection.GetUnsentReports();
                _logger.LogInformation($"Найдено {unsentReports.Count} неотправленных отчетов при запуске");
                
                // Отправляем каждый неотправленный отчет
                foreach (var report in unsentReports)
                {
                    try
                    {
                        _logger.LogInformation($"Отправка неотправленного отчета за период {report.StartDate} - {report.EndDate} (ID: {report.Id})");
                        
                        // Получаем видео для этого периода отчета
                        var videos = await _videoRepository.GetVideosAsync(report.StartDate, report.EndDate);
                        
                        // Отправляем отчет
                        await _telegramBotService.SendVideoGroupAsync(videos, report.StartDate, report.EndDate);
                        
                        _logger.LogInformation($"Неотправленный отчет за период {report.StartDate} - {report.EndDate} (ID: {report.Id}) успешно отправлен");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Ошибка при отправке неотправленного отчета за период {report.StartDate} - {report.EndDate} (ID: {report.Id})");
                    }
                }
                
                if (unsentReports.Count == 0)
                {
                    _logger.LogInformation("Нет неотправленных отчетов для повторной отправки при запуске");
                }
                else
                {
                    _logger.LogInformation($"Завершена отправка {unsentReports.Count} неотправленных отчетов при запуске");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при проверке и отправке неотправленных отчетов при запуске");
            }
        }

        /// <summary>
        /// Проверяет и отправляет отчеты за сегодня при запуске (только для режимов с расписанием)
        /// </summary>
        public async Task CheckAndSendTodaysReportsAsync()
        {
            // Этот метод больше не используется для проверки отчетов при запуске
            // Вместо этого используется SendUnsentReportsAtStartupAsync для отправки неотправленных отчетов
            _logger.LogDebug("CheckAndSendTodaysReportsAsync вызван, но не реализован, так как функциональность перенесена в SendUnsentReportsAtStartupAsync");
            await Task.CompletedTask;
        }
    }
}