namespace CountryTelegramBot.Models
{
    public interface ITelegramBot
    {
    Task StartBot();
    Task SendMessage(long chatId, string message);
    Task SendVideoSafely(string videoPath, string grabPath);
    Task SendVideoGroupAsync(IEnumerable<VideoModel> videos, DateTime start, DateTime end);
    }
}
