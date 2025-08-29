namespace CountryTelegramBot.Models
{
    public interface IVideoWatcher
    {
        // Пример: запуск/остановка наблюдения
        void StartWatching();
        void StopWatching();
        // Добавьте другие методы, которые должны быть доступны для тестирования/замены
    }
}
