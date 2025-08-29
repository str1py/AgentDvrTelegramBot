using System.ComponentModel.DataAnnotations;

namespace CountryTelegramBot.Configs
{
    public class CommonConfig
    {
    [Required]
    public string? WatcherType { get; set; }
    public bool ForcedArmedAtNight { get; set; }
    public bool ForcedArmedAtDay { get; set; }
    }

    public class TelegramBotConfig
    {
    [Required]
    [MinLength(10)]
    public string BotToken { get; set; } = string.Empty;

    [Required]
    [MinLength(5)]
    public string ChatId { get; set; } = string.Empty;
    }

    public class AgentDVRConfig
    {
    [Required]
    [Url]
    public string Url { get; set; } = string.Empty;

    [Required]
    public string User { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
    }

    public class SnapshotWatcherConfig
    {
    [Required]
    public string[] Folders { get; set; } = new string[0];
    }

    public class ConnectionStringsConfig
    {
    [Required]
    [MinLength(10)]
    public string DefaultConnection { get; set; } = string.Empty;
    }
}
