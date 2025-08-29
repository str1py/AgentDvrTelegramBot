namespace CountryTelegramBot.Models
{
    public interface IAgentDVR
    {
        Task InitializeAsync();
        Task RebootDVR();
        Task<bool> GetArmState();
        Task<bool> GetSystemState();
        Task SetArmState(bool state);
        // Добавьте другие методы, которые должны быть доступны для тестирования/замены
    }
}
