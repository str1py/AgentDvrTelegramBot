using Microsoft.Extensions.Logging;
using CountryTelegramBot.Models;
using CountryTelegramBot.Repositories;
using CountryTelegramBot.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CountryTelegramBot.Services
{
    public class ReportService : IReportService, IDisposable
    {
        private readonly IDbConnection _dbConnection;
        private readonly ITelegramBotService _telegramBotService;
        private readonly IVideoRepository _videoRepository;
        private readonly ITimeHelper _timeHelper;
        private readonly ILogger<ReportService> _logger;
        private readonly Timer _timer;
        private bool _disposed = false;

        public ReportService(
            IDbConnection dbConnection,
            ITelegramBotService telegramBotService,
            IVideoRepository videoRepository,
            ITimeHelper timeHelper,
            ILogger<ReportService> logger)
        {
            _dbConnection = dbConnection ?? throw new ArgumentNullException(nameof(dbConnection));
            _telegramBotService = telegramBotService ?? throw new ArgumentNullException(nameof(telegramBotService));
            _videoRepository = videoRepository ?? throw new ArgumentNullException(nameof(videoRepository));
            _timeHelper = timeHelper ?? throw new ArgumentNullException(nameof(timeHelper));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Создаем таймер для периодической проверки неотправленных отчетов
            _timer = new Timer(CheckUnsentReports, null, Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>
        /// Запускает периодическую проверку неотправленных отчетов
        /// </summary>
        public void StartPeriodicCheck()
        {
            _logger.LogInformation("Запуск периодической проверки неотправленных отчетов");
            // Проверяем каждые 10 минут
            _timer.Change(TimeSpan.Zero, TimeSpan.FromMinutes(10));
        }

        /// <summary>
        /// Проверяет и отправляет неотправленные отчеты
        /// </summary>
        private async void CheckUnsentReports(object? state)
        {
            try
            {
                _logger.LogInformation("Проверка неотправленных отчетов");
                var unsentReports = _dbConnection.GetUnsentReports();
                _logger.LogInformation($"Найдено {unsentReports.Count} неотправленных отчетов");

                foreach (var report in unsentReports)
                {
                    try
                    {
                        _logger.LogInformation($"Попытка отправки отчета за период {report.StartDate} - {report.EndDate} (ID: {report.Id})");

                        // Получаем видео для этого периода отчета
                        var videos = await _videoRepository.GetVideosAsync(report.StartDate, report.EndDate);

                        // Отправляем отчет
                        await _telegramBotService.SendVideoGroupAsync(videos, report.StartDate, report.EndDate);

                        _logger.LogInformation($"Отчет за период {report.StartDate} - {report.EndDate} (ID: {report.Id}) успешно отправлен");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Ошибка при отправке отчета за период {report.StartDate} - {report.EndDate} (ID: {report.Id})");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при проверке неотправленных отчетов");
            }
        }

        /// <summary>
        /// Проверяет и отправляет отчеты за сегодня, если они еще не были отправлены
        /// </summary>
        public async Task CheckAndSendTodaysReports(CountryTelegramBot.Services.WatcherType watcherType)
        {
            try
            {
                _logger.LogInformation($"Проверка и отправка отчетов за сегодня для типа наблюдателя: {watcherType}");

                // Проверяем только для типов наблюдателей Morning и MorningAndEvening
                if (watcherType != CountryTelegramBot.Services.WatcherType.Morning && watcherType != CountryTelegramBot.Services.WatcherType.MorningAndEvening)
                {
                    _logger.LogInformation("Для данного типа наблюдателя ежедневные отчеты не требуются");
                    return;
                }

                var wasSent = await WasReportSentToday(watcherType);
                if (!wasSent)
                {
                    _logger.LogInformation("Отчет за сегодня не был отправлен. Отправляю отчет...");

                    // Определяем период времени в зависимости от типа наблюдателя
                    if (watcherType == CountryTelegramBot.Services.WatcherType.Morning)
                    {
                        // Для утренних отчетов отправляем видео за прошлую ночь
                        var startDate = _timeHelper.NightVideoStartDate.AddDays(-1);
                        var endDate = _timeHelper.NightVideoEndDate.AddDays(-1);
                        _logger.LogInformation($"Отправка утреннего отчета за период: {startDate} - {endDate}");

                        // Добавляем запись в БД с IsSent = false перед отправкой
                        await AddPendingReportStatus(startDate, endDate);

                        var videos = await _dbConnection.GetVideosAsync(startDate, endDate);
                        await _telegramBotService.SendVideoGroupAsync(videos, startDate, endDate);
                    }
                    else if (watcherType == CountryTelegramBot.Services.WatcherType.MorningAndEvening)
                    {
                        // Для утренне-вечерних отчетов отправляем видео за дневной и ночной периоды
                        // Отправляем дневные видео (с утра до вечера)
                        var dayStartDate = _timeHelper.DayVideoStartDate;
                        var dayEndDate = _timeHelper.DayVideoEndDate;
                        _logger.LogInformation($"Отправка дневного отчета за период: {dayStartDate} - {dayEndDate}");

                        // Добавляем запись в БД с IsSent = false перед отправкой
                        await AddPendingReportStatus(dayStartDate, dayEndDate);

                        var dayVideos = await _dbConnection.GetVideosAsync(dayStartDate, dayEndDate);
                        await _telegramBotService.SendVideoGroupAsync(dayVideos, dayStartDate, dayEndDate);

                        // Отправляем ночные видео (с вечера до следующего утра)
                        var nightStartDate = _timeHelper.NightVideoStartDate.AddDays(-1);
                        var nightEndDate = _timeHelper.NightVideoEndDate.AddDays(-1);
                        _logger.LogInformation($"Отправка ночного отчета за период: {nightStartDate} - {nightEndDate}");

                        // Добавляем запись в БД с IsSent = false перед отправкой
                        await AddPendingReportStatus(nightStartDate, nightEndDate);

                        var nightVideos = await _dbConnection.GetVideosAsync(nightStartDate, nightEndDate);
                        await _telegramBotService.SendVideoGroupAsync(nightVideos, nightStartDate, nightEndDate);
                    }

                    _logger.LogInformation("Отчеты за сегодня отправлены.");
                }
                else
                {
                    _logger.LogInformation("Отчеты за сегодня уже были отправлены.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при проверке и отправке отчетов за сегодня");
            }
        }

        /// <summary>
        /// Проверяет, был ли отчет отправлен сегодня для указанного типа наблюдателя
        /// </summary>
        private async Task<bool> WasReportSentToday(CountryTelegramBot.Services.WatcherType watcherType)
        {
            try
            {
                var now = DateTime.Now;
                DateTime startDate, endDate;

                // Определяем период времени в зависимости от типа наблюдателя
                if (watcherType == CountryTelegramBot.Services.WatcherType.Morning)
                {
                    // Для утренних отчетов проверяем период прошлой ночи
                    startDate = _timeHelper.NightVideoStartDate.AddDays(-1);
                    endDate = _timeHelper.NightVideoEndDate.AddDays(-1);
                    _logger.LogInformation($"Проверка утреннего отчета за период: {startDate} - {endDate}");
                }
                else if (watcherType == CountryTelegramBot.Services.WatcherType.MorningAndEvening)
                {
                    // Для утренне-вечерних отчетов проверяем, был ли отправлен отчет за дневной или ночной период
                    // Проверяем дневной период (с утра до вечера)
                    startDate = _timeHelper.DayVideoStartDate;
                    endDate = _timeHelper.DayVideoEndDate;
                    _logger.LogInformation($"Проверка дневного отчета за период: {startDate} - {endDate}");

                    // Проверяем, был ли отправлен дневной отчет
                    var dayReportStatus = await _dbConnection.GetReportStatusAsync(startDate, endDate);
                    if (dayReportStatus != null && dayReportStatus.IsSent && dayReportStatus.SentAt.HasValue &&
                        dayReportStatus.SentAt.Value.Date == now.Date)
                    {
                        _logger.LogInformation("Дневной отчет уже был отправлен сегодня");
                        return true;
                    }

                    // Проверяем ночной период (с вечера до следующего утра)
                    startDate = _timeHelper.NightVideoStartDate.AddDays(-1);
                    endDate = _timeHelper.NightVideoEndDate.AddDays(-1);
                    _logger.LogInformation($"Проверка ночного отчета за период: {startDate} - {endDate}");
                }
                else
                {
                    // Для типа ASAP ежедневные отчеты не требуются
                    _logger.LogInformation("Для типа ASAP ежедневные отчеты не требуются");
                    return true;
                }

                // Проверяем, был ли отправлен отчет за определенный период
                var reportStatus = await _dbConnection.GetReportStatusAsync(startDate, endDate);
                var wasSent = reportStatus != null && reportStatus.IsSent && reportStatus.SentAt.HasValue &&
                       reportStatus.SentAt.Value.Date == now.Date;

                if (wasSent)
                {
                    _logger.LogInformation("Отчет уже был отправлен сегодня");
                }
                else
                {
                    _logger.LogInformation("Отчет еще не был отправлен сегодня");
                    if (reportStatus != null)
                    {
                        _logger.LogInformation($"Текущий статус отчета (ID: {reportStatus.Id}): Отправлено={reportStatus.IsSent}, Время отправки={reportStatus.SentAt}");
                    }
                }

                return wasSent;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при проверке отправки отчета сегодня");
                return false; // Предполагаем, что отчет не отправлен, если произошла ошибка
            }
        }

        /// <summary>
        /// Добавляет запись о попытке отправки отчета с IsSent = false
        /// </summary>
        private async Task AddPendingReportStatus(DateTime startDate, DateTime endDate)
        {
            try
            {
                // Проверяем, есть ли уже запись для этого периода отчета
                var existingReportStatus = await _dbConnection.GetReportStatusAsync(startDate, endDate);
                if (existingReportStatus == null)
                {
                    // Создаем новую запись с IsSent = false
                    await _dbConnection.AddReportStatus(startDate, endDate, false, null);
                    _logger.LogInformation($"Добавлена запись о попытке отправки отчета: {startDate} - {endDate}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при добавлении записи о попытке отправки отчета");
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _timer?.Dispose();
                _disposed = true;
            }
        }
    }

    public enum WatcherType
    {
        ASAP,
        Morning,
        MorningAndEvening
    }
}