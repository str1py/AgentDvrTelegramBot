using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Microsoft.Extensions.Logging;

namespace CountryTelegramBot
{
    public class TelegramBot : IDisposable
    {
        private TelegramBotClient bot;
        private CancellationTokenSource? cts;

        private string botToken;
        private string chatId;
        private AgentDVR agent;
        private bool disposed;
        private readonly ILogger logger;
        private FileHelper fileHelper;
        private DbConnection dbConnection;

        public TelegramBot(string botToken, string chatId, AgentDVR agent, DbConnection dbConnection, ILogger logger)
        {
            this.botToken = botToken;
            this.chatId = chatId;
            this.logger = logger;
            this.dbConnection = dbConnection;
            this.agent = agent;
            fileHelper = new FileHelper(logger);
            cts = new CancellationTokenSource();
            bot = new TelegramBotClient(botToken, cancellationToken: cts.Token);
        }
        public async Task StartBot()
        {
            bot.OnError += OnError;
            bot.OnMessage += OnMessage;
            bot.OnUpdate += OnUpdate;
            var me = await bot.GetMe();
            logger.LogInformation($"@{me.Username} is running...");
            Console.ReadLine();
            cts?.Cancel(); // stop the bot
        }

        private async Task OnError(Exception exception, HandleErrorSource source)
        {
            logger.LogError(exception, "TelegramBot error");
            await Task.CompletedTask;
        }
        private async Task OnMessage(Message msg, UpdateType type)
        {
            var me = await bot.GetMe();
            logger.LogInformation($"Received text '{msg.Text}' in {msg.Chat}");
            await OnCommand(msg.Text ?? string.Empty, msg); // null-safe
        }

        private async Task OnCommand(string command, Message msg)
        {
            var chatId = msg.Chat.Id;
            logger.LogInformation($"Received command: {command}");
            switch (command)
            {
                case "/menu":
                case "меню":
                    await ShowUserMenu(chatId);
                    break;
                case "/adminmenu":
                    await ShowMainMenu(chatId);
                    break;

                default:
                    await bot.SendMessage(chatId,
                        "Доступные команды:\n menu - Включить охрану\n");
                    break;
            }
        }

        private async Task OnUpdate(Update update)
        {
            var callback = update.CallbackQuery;
            var chatId = callback?.Message?.Chat?.Id ?? 0;
            if (callback != null && callback.Data == "1.1")
            {
                await agent.SetArmState(true);
                await bot.SendMessage(chatId, "✅ Охрана включена! Система активирована.");
            }
            else if (callback != null && callback.Data == "2.1")
            {
                await agent.SetArmState(false);
                await bot.SendMessage(chatId, "⛔ Охрана выключена! Система деактивирована.");
            }
            else if (callback != null && callback.Data == "3.1")
            {
                var status = await agent.GetArmState();
                await bot.SendMessage(chatId,
                        $"🔒 Статус охраны: {(status ? "ВКЛЮЧЕНА" : "ВЫКЛЮЧЕНА")}");
            }
            else if (callback != null && callback.Data == "4.1")
            {
                var status = await agent.GetSystemState();
                await bot.SendMessage(chatId,
                        $"🔒 Статус системы: {(status ? "ВКЛЮЧЕНА" : "ВЫКЛЮЧЕНА")}");
            }
            else if (callback != null && callback.Data == "5.1")
            {
                await agent.RebootDVR();
                await bot.SendMessage(chatId,
                        $"🔒 Статус системы: Производится перезагрузка");
            }
        }

        private InlineKeyboardMarkup GetMainMenuKeyboard()
        {
            return new InlineKeyboardMarkup(new[] {
                new [] {
                InlineKeyboardButton.WithCallbackData("🔒 Включить охрану","1.1")
                },
                new [] {
                InlineKeyboardButton.WithCallbackData("🔓 Выключить охрану","2.1")
                },
                new [] {
                InlineKeyboardButton.WithCallbackData("📊 Статус охраны","3.1")
                },
                new [] {
                InlineKeyboardButton.WithCallbackData("📊 Статус системы","4.1")
                },
                new []{
                InlineKeyboardButton.WithCallbackData("📊 Перезагрузить систему","5.1")
                }
            });
        }

        private async Task<InlineKeyboardMarkup> GetUserMenuKeyboard()
        {
            if (await agent.GetArmState())
            {
                return new InlineKeyboardMarkup(new[] {
                    new [] {
                    InlineKeyboardButton.WithCallbackData("🔓 Выключить охрану","2.1")
                    },
                });
            }
            else
            {
                return new InlineKeyboardMarkup(new[] {
                    new [] {
                    InlineKeyboardButton.WithCallbackData("🔒 Включить охрану","1.1")
                    },
                }); 
            }
        }
        private async Task ShowMainMenu(long chatId)
        {
             await bot.SendMessage(
                chatId,
                $"{await agent.GetArmStateMessage()}\nВыберите действие:",
                replyMarkup: GetMainMenuKeyboard());
        }

        private async Task ShowUserMenu(long chatId)
        {
            await bot.SendMessage(
                chatId,
                $"{await agent.GetArmStateMessage()}\nВыберите действие:",
                replyMarkup: await GetUserMenuKeyboard());
        }

        private async Task OnCallbackQuery(CallbackQuery callbackQuery)
        {
            await bot.AnswerCallbackQuery(callbackQuery.Id, $"You selected {callbackQuery.Data}");
            await bot.SendMessage(callbackQuery.Message!.Chat, $"Received callback from inline button {callbackQuery.Data}");
        }

    
        public async Task SendVideoSafely(string videoPath, string photoPath)
        {
            string message = "⚠️Обнаружено движение!";

            var videoStream = await fileHelper.GetFileStreamFromVideo(videoPath);
            if (videoStream != null)
            {
                await using var photoStream = File.OpenRead(photoPath);
                // Отправляем видео в Telegram
                await bot.SendVideo(
                    chatId: chatId,
                    video: InputFile.FromStream(videoStream, Path.GetFileName(videoPath)),
                    thumbnail: InputFile.FromStream(photoStream, "preview.jpg"),
                    //duration: GetVideoDuration(videoPath), // Должен быть реализован
                    width: 1920,                          // Опционально
                    height: 1080,                         // Опционально
                    supportsStreaming: true,
                    caption: message,
                    parseMode: ParseMode.Html
                    );
            }
            else
            {
                if (await dbConnection.RemoveItemByPath(videoPath))
                    logger?.LogWarning($"Данные удалены из базы данных: {videoPath}");
            }
        }

        public async Task SendVideoGroupAsync(List<VideoModel> video, DateTime startDate, DateTime endDate)
        {
            if (video.Count() == 0)
            {
                await bot.SendMessage(
                    chatId: chatId,
                    text: $"Тревог не зафиксировано с {startDate.ToShortDateString()} {startDate.ToShortTimeString()} по {endDate.ToShortDateString()} {endDate.ToShortTimeString()}",
                    parseMode: ParseMode.Html
                );
            }
            else
            {
                await bot.SendMessage(
                        chatId: chatId,
                        text: $"Отправляю отчет за период с {startDate.ToShortDateString()} {startDate.ToShortTimeString()} по {endDate.ToShortDateString()} {endDate.ToShortTimeString()}",
                        parseMode: ParseMode.Html
                    );

                const int maxGroupSize = 10;
                var groups = video
                    .Select((path, index) => new { path, index })
                    .GroupBy(x => x.index / maxGroupSize, x => x.path);

                var alreadeSendedVideos = 0;
                foreach (var group in groups)
                {
                    try
                    {
                        alreadeSendedVideos += group.Count();
                        logger?.LogInformation($"Начинаю отправку {alreadeSendedVideos}/{video.Count()} видео");
                        var media = new List<IAlbumInputMedia>();
                        foreach (var vid in group)
                        {
                            // Открываем каждый файл видео
                            var videoStream = await fileHelper.GetFileStreamFromVideo(vid.Path);
                            if (videoStream != null)
                            {
                                // Создаем медиа-объект для альбома
                                media.Add(new InputMediaVideo(
                                    media: InputFile.FromStream(videoStream, Path.GetFileName(vid.Path))
                                ));
                            }
                            else
                            {
                                if (await dbConnection.RemoveItemByPath(vid.Path))
                                    logger?.LogWarning($"Данные удалены из базы данных: {vid.Path}");
                            }
                        }

                        // Отправляем группу видео
                        await bot.SendMediaGroup(
                            chatId: chatId,
                            media: media
                        );
                       
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Ошибка отправки видеоальбома: {ex.Message}");
                    }
                }
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            cts?.Cancel();
            cts?.Dispose();
            (bot as IDisposable)?.Dispose();
            disposed = true;
        }
    }
}
