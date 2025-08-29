using Microsoft.Extensions.Logging;

namespace CountryTelegramBot
{
    using CountryTelegramBot.Models;

    public class TimeHelper : ITimeHelper
    {
        public DateTime MorningReport { get; private set; }
        public DateTime EveningReport { get; private set; }
        public DateTime NightVideoStartDate { get; private set; }
        public DateTime NightVideoEndDate { get; private set; }
        public DateTime DayVideoStartDate { get; private set; }
        public DateTime DayVideoEndDate { get; private set; }

        private readonly TimeSpan morningReportTime = new TimeSpan(8, 00, 0);   // 8:00
        private readonly TimeSpan eveningReportTime = new TimeSpan(23, 0, 0); // 23:00
        public TimeSpan ForcedArmedNightTime { get; } = new TimeSpan(23, 0, 0); // 23:00
        public TimeSpan ForcedArmedDayTime { get; } = new TimeSpan(8, 0, 0);   // 8:00;

        private readonly ILogger? logger;
        private readonly IErrorHandler? errorHandler;

        public TimeHelper(ILogger? logger, IErrorHandler? errorHandler = null)
        {
            this.logger = logger;
            this.errorHandler = errorHandler;
            CalculateNextTimes();
        }


        private void CalculateNextTimes()
        {
            CalculateNextMorningReport();
            CalculateNextEveningReport();
            CalculateNextNightPeriod();
            CalculateNextDayPeriod();
            logger?.LogInformation($"Следующий утренний отчет: {MorningReport}. За период с {NightVideoStartDate} по {NightVideoEndDate}");
            logger?.LogInformation($"Следующий вечерний отчет: {EveningReport}. За период с {DayVideoStartDate} по {DayVideoEndDate}");
        }


        public void CalculateNextMorningReport()
        {
            var now = DateTime.Now;
            MorningReport = now.Date.AddDays(1).Add(morningReportTime);
        }
        public void CalculateNextEveningReport()
        {
            var now = DateTime.Now;
            var today8am = now.Date.Add(morningReportTime);
            var today9pm = now.Date.Add(eveningReportTime);
            var tomorrow8am = now.Date.AddDays(1).Add(morningReportTime);
            EveningReport = now < today8am ? today8am :
                        now < today9pm ? today9pm :
                        tomorrow8am;
        }
        public void CalculateNextNightPeriod()
        {
            var now = DateTime.Now;
            NightVideoStartDate = now.Date.Add(eveningReportTime);
            NightVideoEndDate = now.Date.AddDays(1).Add(morningReportTime);
        }
        public void CalculateNextDayPeriod()
        {
            var now = DateTime.Now;
            DayVideoStartDate = now.Date.Add(morningReportTime);
            DayVideoEndDate = now.Date.Add(eveningReportTime);
        }

    }
}