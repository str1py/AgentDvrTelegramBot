using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CountryTelegramBot.Models;

namespace CountryTelegramBot.Services
{
    public interface IReportService : IDisposable
    {
        /// <summary>
        /// Запускает периодическую проверку неотправленных отчетов
        /// </summary>
        void StartPeriodicCheck();
        
        /// <summary>
        /// Проверяет и отправляет отчеты за сегодня, если они еще не были отправлены
        /// </summary>
        Task CheckAndSendTodaysReports(WatcherType reportType);
        
        /// <summary>
        /// Получает тип наблюдателя из строки
        /// </summary>
        WatcherType GetWatcherType(string type);
    }
}