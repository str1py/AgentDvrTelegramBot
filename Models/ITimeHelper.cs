namespace CountryTelegramBot.Models
{
    public interface ITimeHelper
    {
        DateTime MorningReport { get; }
        DateTime EveningReport { get; }
        DateTime NightVideoStartDate { get; }
        DateTime NightVideoEndDate { get; }
        DateTime DayVideoStartDate { get; }
        DateTime DayVideoEndDate { get; }
        void CalculateNextMorningReport();
        void CalculateNextEveningReport();
        void CalculateNextNightPeriod();
        void CalculateNextDayPeriod();
    }
}
