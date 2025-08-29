namespace CountryTelegramBot.Models
{
    public interface ITelegramBot
    {
        Task StartBot();
        Task SendMessage(long chatId, string message);
        // Добавьте другие методы, которые должны быть доступны для тестирования/замены
    }
}
