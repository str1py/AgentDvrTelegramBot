using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using System.Data.Common;
using MySqlConnector;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace CountryTelegramBot
{
    [Table(name:"video")]
    public class VideoModel
    {
        [Key]
        public int Id { get; set; }
        public string? Path { get; set; }
        public string? Grab { get; set; }
        public DateTime Date { get; set; }

    }
}
