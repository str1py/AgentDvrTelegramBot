namespace CountryTelegramBot.Repositories
{
    /// <summary>
    /// Интерфейс для работы с видеозаписями в базе данных
    /// </summary>
    public interface IVideoRepository
    {
        /// <summary>
        /// Добавляет новую видеозапись в базу данных
        /// </summary>
        /// <param name="path">Путь к видеофайлу</param>
        /// <param name="grab">Путь к изображению превью</param>
        /// <returns>Задача добавления записи</returns>
        Task AddVideoAsync(string path, string grab);

        /// <summary>
        /// Получает список видеозаписей за указанный период
        /// </summary>
        /// <param name="startDate">Начальная дата</param>
        /// <param name="endDate">Конечная дата</param>
        /// <returns>Список видеозаписей</returns>
        Task<List<VideoModel>> GetVideosAsync(DateTime startDate, DateTime endDate);

        /// <summary>
        /// Получает последнюю добавленную видеозапись
        /// </summary>
        /// <returns>Последняя видеозапись или null, если записей нет</returns>
        Task<VideoModel?> GetLastVideoAsync();

        /// <summary>
        /// Удаляет видеозапись по указанному пути
        /// </summary>
        /// <param name="path">Путь к видеофайлу</param>
        /// <returns>true, если запись была удалена; false, если запись не найдена</returns>
        Task<bool> RemoveByPathAsync(string path);
    }
}