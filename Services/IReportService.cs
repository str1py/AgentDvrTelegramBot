using System.Threading.Tasks;

namespace CountryTelegramBot.Services
{
    public interface IReportService
    {
        /// <summary>
        /// Запускает периодическую проверку неотправленных отчетов
        /// </summary>
        void StartPeriodicCheck();

        /// <summary>
        /// Проверяет и отправляет отчеты за сегодня, если они еще не были отправлены
        /// </summary>
        Task CheckAndSendTodaysReports(CountryTelegramBot.Services.WatcherType watcherType);
    }
}