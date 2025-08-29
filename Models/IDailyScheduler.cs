namespace CountryTelegramBot.Models
{
    public interface IDailyScheduler : IDisposable
    {
        // Пример: запуск/остановка таймера
        void StartTimer();
    }
}
