﻿using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Microsoft.Extensions.Logging;
using CountryTelegramBot.Repositories;
using CountryTelegramBot.Services;
using System.Text;

namespace CountryTelegramBot
{

    public class TelegramBot : ITelegramBotService, IDisposable
    {
        // Реализация метода интерфейса ITelegramBot
        public async Task SendMessage(long chatId, string text)
        {
            await bot.SendMessage(chatId, text);
        }
        private TelegramBotClient bot;
        private CancellationTokenSource? cts;

        private string botToken;
        private string chatId;
        private AgentDVR agent;
        private bool disposed;
        private readonly ILogger<TelegramBot> logger;
        private FileHelper fileHelper;
        private IVideoRepository videoRepository;

        public TelegramBot(string botToken, string chatId, AgentDVR agent, IVideoRepository videoRepository, FileHelper fileHelper, ILogger<TelegramBot> logger)
        {
            this.botToken = botToken ?? throw new ArgumentNullException(nameof(botToken));
            this.chatId = chatId ?? throw new ArgumentNullException(nameof(chatId));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.videoRepository = videoRepository ?? throw new ArgumentNullException(nameof(videoRepository));
            this.agent = agent ?? throw new ArgumentNullException(nameof(agent));
            this.fileHelper = fileHelper ?? throw new ArgumentNullException(nameof(fileHelper));
            cts = new CancellationTokenSource();
            bot = new TelegramBotClient(botToken, cancellationToken: cts.Token);
            logger.LogInformation($"TelegramBot создан. Token: {MaskSecret(botToken)}, ChatId: {MaskSecret(chatId)}");

        }

        private string MaskSecret(string secret)
        {
            if (string.IsNullOrEmpty(secret)) return "[empty]";
            if (secret.Length <= 4) return "****";
            return secret.Substring(0, 2) + new string('*', secret.Length - 4) + secret.Substring(secret.Length - 2);
        }

        public async Task StartBot()
        {
            try
            {
                // First, delete any existing webhook to avoid conflicts
                try
                {
                    await bot.DeleteWebhook();
                    logger.LogInformation("Existing webhook deleted successfully");
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to delete webhook, but continuing anyway");
                }

                bot.OnError += OnError;
                bot.OnMessage += OnMessage;
                bot.OnUpdate += OnUpdate;
                var me = await bot.GetMe();
                logger.LogInformation($"@{me.Username} is running...");
                Console.ReadLine();
                cts?.Cancel(); // stop the bot
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ошибка запуска TelegramBot");
            }
        }

        private async Task OnError(Exception exception, HandleErrorSource source)
        {
            logger.LogError(exception, "TelegramBot error");
            await Task.CompletedTask;
        }
        private async Task OnMessage(Message msg, UpdateType type)
        {
            try
            {
                var me = await bot.GetMe();
                logger.LogInformation($"Received text '{msg.Text}' in {msg.Chat}");
                await OnCommand(msg.Text ?? string.Empty, msg); // null-safe
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ошибка обработки сообщения TelegramBot");
            }
        }

        private async Task OnCommand(string command, Message msg)
        {
            try
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
            catch (Exception ex)
            {
                logger.LogError(ex, $"Ошибка обработки команды TelegramBot: {command}");
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


        public async Task SendVideoSafely(string videoPath, string grabPath)
        {
            string message = "⚠️Обнаружено движение!";

            var videoStream = await fileHelper.GetFileStreamFromVideo(videoPath);
            if (videoStream != null)
            {
                await using var photoStream = File.OpenRead(grabPath);
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
                if (await videoRepository.RemoveByPathAsync(videoPath))
                    logger?.LogWarning($"Данные удалены из базы данных: {videoPath}");
            }
        }

        public async Task SendVideoGroupAsync(IEnumerable<VideoModel> videos, DateTime start, DateTime end)
        {
            var videoList = videos.ToList();
            if (videoList.Count == 0)
            {
                await bot.SendMessage(
                    chatId: chatId,
                    text: $"Тревог не зафиксировано с {start.ToShortDateString()} {start.ToShortTimeString()} по {end.ToShortDateString()} {end.ToShortTimeString()}",
                    parseMode: ParseMode.Html
                );
            }
            else
            {
                await bot.SendMessage(
                        chatId: chatId,
                        text: $"Отправляю отчет за период с {start.ToShortDateString()} {start.ToShortTimeString()} по {end.ToShortDateString()} {end.ToShortTimeString()}",
                        parseMode: ParseMode.Html
                    );

                const int maxGroupSize = 10;
                var groups = videoList
                    .Select((path, index) => new { path, index })
                    .GroupBy(x => x.index / maxGroupSize, x => x.path);

                var alreadeSendedVideos = 0;
                foreach (var group in groups)
                {
                    try
                    {
                        alreadeSendedVideos += group.Count();
                        logger?.LogInformation($"Начинаю отправку {alreadeSendedVideos}/{videoList.Count} видео");
                        var media = new List<IAlbumInputMedia>();
                        foreach (var vid in group)
                        {
                            if (vid?.Path != null)
                            {
                                var videoStream = await fileHelper.GetFileStreamFromVideo(vid.Path);
                                if (videoStream != null)
                                {
                                    media.Add(new InputMediaVideo(InputFile.FromStream(videoStream, Path.GetFileName(vid.Path))));
                                }
                                else
                                {
                                    if (await videoRepository.RemoveByPathAsync(vid.Path))
                                        logger?.LogWarning($"Данные удалены из базы данных: {vid.Path}");
                                }
                            }
                            else
                            {
                                if (await videoRepository.RemoveByPathAsync(vid.Path))
                                    logger?.LogWarning($"Данные удалены из базы данных: {vid.Path}");
                            }
                        }
                        await bot.SendMediaGroup(chatId, media);
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