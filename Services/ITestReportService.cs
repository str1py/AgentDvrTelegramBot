using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CountryTelegramBot.Services
{
    public interface ITestReportService
    {
        /// <summary>
        /// Отправляет тестовый отчет
        /// </summary>
        Task SendTestReportAsync();
        
        /// <summary>
        /// Проверяет и отправляет отчеты за сегодня, если они еще не были отправлены
        /// </summary>
        Task CheckAndSendTodaysReportsAsync();
    }
}