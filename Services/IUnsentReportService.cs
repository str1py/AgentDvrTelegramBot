using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CountryTelegramBot.Services
{
    public interface IUnsentReportService
    {
        /// <summary>
        /// Проверяет и отправляет неотправленные отчеты при запуске приложения
        /// </summary>
        Task SendUnsentReportsAtStartupAsync();
        
        /// <summary>
        /// Проверяет и отправляет отчеты за сегодня, если они еще не были отправлены
        /// </summary>
        Task CheckAndSendTodaysReportsAsync();
    }
}