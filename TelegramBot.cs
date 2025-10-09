﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Microsoft.Extensions.Logging;
using CountryTelegramBot.Repositories;
using CountryTelegramBot.Services;
using CountryTelegramBot.Models;
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
        private IDbConnection dbConnection;
        private IVideoCompressionService videoCompressionService;

        public TelegramBot(string botToken, string chatId, AgentDVR agent, IVideoRepository videoRepository, FileHelper fileHelper, ILogger<TelegramBot> logger, IDbConnection dbConnection, IVideoCompressionService videoCompressionService)
        {
            this.botToken = botToken ?? throw new ArgumentNullException(nameof(botToken));
            this.chatId = chatId ?? throw new ArgumentNullException(nameof(chatId));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.videoRepository = videoRepository ?? throw new ArgumentNullException(nameof(videoRepository));
            this.agent = agent ?? throw new ArgumentNullException(nameof(agent));
            this.fileHelper = fileHelper ?? throw new ArgumentNullException(nameof(fileHelper));
            this.dbConnection = dbConnection ?? throw new ArgumentNullException(nameof(dbConnection));
            this.videoCompressionService = videoCompressionService ?? throw new ArgumentNullException(nameof(videoCompressionService));
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
            logger?.LogInformation($"Отправка группы видео: {start} - {end}");
            
            var videoList = videos.ToList();
            if (videoList.Count == 0)
            {
                logger?.LogInformation("Нет видео для отправки в отчете");
                await bot.SendMessage(
                    chatId: chatId,
                    text: $"Тревог не зафиксировано с {start.ToShortDateString()} {start.ToShortTimeString()} по {end.ToShortDateString()} {end.ToShortTimeString()}",
                    parseMode: ParseMode.Html
                );
                return;
            }
            
            logger?.LogInformation($"Отправка отчета с {videoList.Count} видео");
            await bot.SendMessage(
                chatId: chatId,
                text: $"Отправляю отчет за период с {start.ToShortDateString()} {start.ToShortTimeString()} по {end.ToShortDateString()} {end.ToShortTimeString()}",
                parseMode: ParseMode.Html
            );

            const int maxGroupSize = 10;
            var groups = videoList
                .Select((video, index) => new { video, index })
                .GroupBy(x => x.index / maxGroupSize, x => x.video);

            var alreadySentVideos = 0;
            bool sendSuccess = true;
            string? errorMessage = null;
            var processedVideos = new List<string>(); // Track processed videos for cleanup
            
            try
            {
                foreach (var group in groups)
                {
                    var media = new List<IAlbumInputMedia>();
                    var groupProcessedVideos = new List<string>(); // Track videos processed in this group
                    
                    foreach (var vid in group)
                    {
                        if (vid?.Path == null) continue;
                        
                        try
                        {
                            // Централизованная проверка и сжатие видео при необходимости
                            var maxSizeBytes = 50 * 1024 * 1024; // 50 МБ лимит Telegram
                            var processedVideoPath = await videoCompressionService.CompressVideoIfNeededAsync(vid.Path, maxSizeBytes);
                            
                            if (processedVideoPath != null)
                            {
                                // Используем обработанное видео (оригинал или сжатое)
                                var videoStream = await fileHelper.GetFileStreamFromVideo(processedVideoPath);
                                if (videoStream != null)
                                {
                                    media.Add(new InputMediaVideo(InputFile.FromStream(videoStream, Path.GetFileName(processedVideoPath))));
                                    groupProcessedVideos.Add(processedVideoPath);
                                    
                                    // Если это сжатое видео, отслеживаем его для последующей очистки
                                    if (processedVideoPath != vid.Path)
                                    {
                                        processedVideos.Add(processedVideoPath);
                                    }
                                }
                                else
                                {
                                    // Файл недоступен, удаляем из базы данных
                                    if (await videoRepository.RemoveByPathAsync(vid.Path))
                                        logger?.LogWarning($"Данные удалены из базы данных: {vid.Path}");
                                }
                            }
                            else
                            {
                                // Не удалось обработать видео, удаляем из базы данных
                                if (await videoRepository.RemoveByPathAsync(vid.Path))
                                    logger?.LogWarning($"Данные удалены из базы данных: {vid.Path}");
                            }
                        }
                        catch (Exception ex)
                        {
                            logger?.LogError(ex, $"Ошибка обработки видео: {vid.Path}");
                            // Продолжаем обработку других видео в группе
                        }
                    }
                    
                    // Отправляем группу медиа только если она содержит элементы
                    if (media.Count > 0)
                    {
                        try
                        {
                            logger?.LogInformation($"Начинаю отправку группы из {media.Count} видео");
                            await bot.SendMediaGroup(chatId, media);
                            alreadySentVideos += media.Count;
                            logger?.LogInformation($"Успешно отправлено {media.Count} видео в группе");
                        }
                        catch (Exception ex)
                        {
                            sendSuccess = false;
                            errorMessage = $"Ошибка отправки группы видео: {ex.Message}";
                            logger?.LogError(ex, "Ошибка отправки видеоальбома");
                        }
                    }
                    else
                    {
                        logger?.LogWarning("Пропущена отправка группы медиа, так как ни одно видео не доступно или не соответствует требованиям");
                    }
                }
                
                // Сообщаем о количестве отправленных видео
                if (alreadySentVideos == 0 && videoList.Count > 0)
                {
                    sendSuccess = false;
                    errorMessage = "Все видео из отчета недоступны, превышают допустимый размер или были удалены из базы данных.";
                    await bot.SendMessage(
                        chatId: chatId,
                        text: $"⚠️ {errorMessage}",
                        parseMode: ParseMode.Html
                    );
                }
                else if (alreadySentVideos < videoList.Count)
                {
                    await bot.SendMessage(
                        chatId: chatId,
                        text: $"ℹ️ Отправлено {alreadySentVideos} из {videoList.Count} видео. Недоступные видео или видео, превышающие допустимый размер, были удалены из базы данных.",
                        parseMode: ParseMode.Html
                    );
                }
                else
                {
                    await bot.SendMessage(
                        chatId: chatId,
                        text: $"✅ Отчет успешно отправлен. Отправлено {alreadySentVideos} видео.",
                        parseMode: ParseMode.Html
                    );
                }
            }
            catch (Exception ex)
            {
                sendSuccess = false;
                errorMessage = $"Критическая ошибка при отправке отчета: {ex.Message}";
                logger?.LogError(ex, "Критическая ошибка при отправке отчета");
                
                // Сообщаем об ошибке пользователю
                await bot.SendMessage(
                    chatId: chatId,
                    text: $"❌ Критическая ошибка при отправке отчета: {ex.Message}",
                    parseMode: ParseMode.Html
                );
            }
            finally
            {
                // Очищаем временные сжатые файлы
                foreach (var processedVideoPath in processedVideos)
                {
                    try
                    {
                        if (File.Exists(processedVideoPath))
                        {
                            File.Delete(processedVideoPath);
                            logger?.LogInformation($"Временный файл удален: {processedVideoPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.LogWarning(ex, $"Ошибка удаления временного файла: {processedVideoPath}");
                    }
                }
            }
            
            // Сохраняем статус отправки отчета в базе данных
            try
            {
                logger?.LogInformation($"Сохранение статуса отправки отчета в БД: {start} - {end}, Успешно: {sendSuccess}, Ошибка: {errorMessage}");
                
                // Проверяем, есть ли уже запись для этого периода отчета
                var existingReportStatus = dbConnection.GetReportStatus(start, end);
                if (existingReportStatus != null)
                {
                    // Обновляем существующую запись
                    logger?.LogInformation($"Обновление существующей записи о статусе отчета (ID: {existingReportStatus.Id})");
                    await dbConnection.UpdateReportStatus(existingReportStatus.Id, sendSuccess, errorMessage);
                }
                else
                {
                    // Создаем новую запись
                    logger?.LogInformation("Создание новой записи о статусе отчета");
                    await dbConnection.AddReportStatus(start, end, sendSuccess, errorMessage);
                }
                
                logger?.LogInformation($"Статус отправки отчета сохранен: Успешно={sendSuccess}, Ошибка={errorMessage}");
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Ошибка при сохранении статуса отправки отчета");
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