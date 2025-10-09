using System.Threading.Tasks;

namespace CountryTelegramBot.Services
{
    public interface IAppInitializationService
    {
        /// <summary>
        /// Инициализирует приложение
        /// </summary>
        Task InitializeAsync();
    }
}