namespace CountryTelegramBot.Configs
{
    public class CommonConfig
    {
        public string? WatcherType { get; set; }
        public bool ForcedArmedAtNight { get; set; }
        public bool ForcedArmedAtDay { get; set; }
    }

    public class TelegramBotConfig
    {
        public string BotToken { get; set; } = string.Empty;
        public string ChatId { get; set; } = string.Empty;
    }

    public class AgentDVRConfig
    {
        public string Url { get; set; } = string.Empty;
        public string User { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class SnapshotWatcherConfig
    {
        public string[] Folders { get; set; } = new string[0];
    }

    public class ConnectionStringsConfig
    {
        public string DefaultConnection { get; set; } = string.Empty;
    }
}
