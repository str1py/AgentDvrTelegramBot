namespace CountryTelegramBot.Services
{
    /// <summary>
    /// Интерфейс для сервиса Telegram бота
    /// </summary>
    public interface ITelegramBotService
    {
        /// <summary>
        /// Запускает Telegram бота
        /// </summary>
        /// <returns>Задача запуска бота</returns>
        Task StartBot();

        /// <summary>
        /// Безопасно отправляет видео в Telegram
        /// </summary>
        /// <param name="videoPath">Путь к видеофайлу</param>
        /// <param name="photoPath">Путь к превью изображению</param>
        /// <returns>Задача отправки видео</returns>
        Task SendVideoSafely(string videoPath, string photoPath);

        /// <summary>
        /// Отправляет группу видео в Telegram
        /// </summary>
        /// <param name="videos">Список видеозаписей</param>
        /// <param name="startDate">Начальная дата периода</param>
        /// <param name="endDate">Конечная дата периода</param>
        /// <returns>Задача отправки группы видео</returns>
        Task SendVideoGroupAsync(List<VideoModel> videos, DateTime startDate, DateTime endDate);
    }
}