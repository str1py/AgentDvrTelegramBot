using Microsoft.Extensions.Logging;

namespace CountryTelegramBot
{
    public class TimeHelper
    {
        public DateTime morningReport;
        public DateTime eveningReport;
        private TimeSpan morningReportTime = new TimeSpan(8, 00, 0);   // 8:00
        private TimeSpan eveningReportTime = new TimeSpan(23, 0, 0); // 23:00

        public DateTime NightVideoStartDate;
        public DateTime NightVideoEndDate;

        public DateTime DayVideoStartDate;
        public DateTime DayVideoEndDate;

        public TimeSpan forcedArmedNightTime = new TimeSpan(23, 0, 0); // 23:00
        public TimeSpan forcedArmedDayTime = new TimeSpan(8, 0, 0);   // 8:00;

        private ILogger? logger;

        public TimeHelper(ILogger? logger)
        {
            this.logger = logger;
            CalculateNextTimes();
        }


        private void CalculateNextTimes()
        {
            CalculateNextMorningReport();
            CalculateNextEveningReport();

            CalculateNextNightPeriod();
            CalculateNextDayPeriod();

            logger?.LogInformation($"Следующий утренний отчет: {morningReport}. За период с {NightVideoStartDate} по {NightVideoEndDate}");
            logger?.LogInformation($"Следующий вечерний отчет: {eveningReport}. За период с {DayVideoStartDate} по {DayVideoEndDate}");
        }


        public void CalculateNextMorningReport()
        {
            var now = DateTime.Now;
            // Действие 1: следующее выполнение в 8:00 следующего дня
            morningReport = now.Date.AddDays(1).Add(morningReportTime);
        }
        public void CalculateNextEveningReport()
        {
            var now = DateTime.Now;
            // Действие 1: следующее выполнение в 8:00 следующего дня
            // Действие 2: следующее выполнение в ближайшее из двух времен
            var today8am = now.Date.Add(morningReportTime);
            var today9pm = now.Date.Add(eveningReportTime);
            var tomorrow8am = now.Date.AddDays(1).Add(morningReportTime);

            eveningReport = now < today8am ? today8am :
                        now < today9pm ? today9pm :
                        tomorrow8am;
        }
        public void CalculateNextNightPeriod()
        {
            var now = DateTime.Now;
            // c 23 вечера предыдущего дня до 8 утра текущего дня 
            NightVideoStartDate = now.Date.Add(eveningReportTime);
            NightVideoEndDate = now.Date.AddDays(1).Add(morningReportTime);
        }
        public void CalculateNextDayPeriod()
        {
            var now = DateTime.Now;
            // c 8 утра до 23 вечера этого дня
            DayVideoStartDate = now.Date.Add(morningReportTime);
            DayVideoEndDate = now.Date.Add(eveningReportTime);
        }

    }
}