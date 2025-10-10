﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿using Telegram.Bot;
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
        private IFileHelper fileHelper;
        private IVideoRepository videoRepository;
        private IDbConnection dbConnection;
        private IVideoCompressionService videoCompressionService;

        public TelegramBot(string botToken, string chatId, AgentDVR agent, IVideoRepository videoRepository, IFileHelper fileHelper, ILogger<TelegramBot> logger, IDbConnection dbConnection, IVideoCompressionService videoCompressionService)
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
                // Отладочная информация о видео в отчете
                logger?.LogInformation($"=== НАЧАЛО ОБРАБОТКИ ОТЧЕТА ===");
                logger?.LogInformation($"Общее количество видео в отчете: {videoList.Count}");
                
                foreach (var vid in videoList)
                {
                    if (vid?.Path != null)
                    {
                        logger?.LogInformation($"Видео для обработки: {vid.Path}");
                        
                        // Проверяем существование файла
                        if (File.Exists(vid.Path))
                        {
                            var fileInfo = new FileInfo(vid.Path);
                            logger?.LogInformation($"Файл существует. Размер: {fileInfo.Length} байт ({fileInfo.Length / (1024.0 * 1024.0):F2} МБ)");
                        }
                        else
                        {
                            logger?.LogWarning($"Файл НЕ существует: {vid.Path}");
                        }
                    }
                    else
                    {
                        logger?.LogWarning("Обнаружен элемент видео с пустым путем");
                    }
                }
                
                foreach (var group in groups)
                {
                    var media = new List<IAlbumInputMedia>();
                    var groupProcessedVideos = new List<string>(); // Track videos processed in this group
                    
                    foreach (var vid in group)
                    {
                        if (vid?.Path == null) 
                        {
                            logger?.LogWarning("Пропущен элемент видео с пустым путем");
                            continue;
                        }
                        
                        logger?.LogInformation($"Обработка видео: {vid.Path}");
                        
                        try
                        {
                            // Проверяем существование файла перед обработкой
                            if (!File.Exists(vid.Path))
                            {
                                logger?.LogWarning($"Файл видео не существует: {vid.Path}");
                                // Файл не существует, удаляем из базы данных
                                if (await videoRepository.RemoveByPathAsync(vid.Path))
                                    logger?.LogWarning($"Данные удалены из базы данных (файл не существует): {vid.Path}");
                                continue;
                            }
                            
                            // Получаем информацию о размере файла
                            var fileInfo = new FileInfo(vid.Path);
                            logger?.LogInformation($"Размер исходного файла: {fileInfo.Length} байт ({fileInfo.Length / (1024.0 * 1024.0):F2} МБ)");
                            
                            // Централизованная проверка и сжатие видео до 5 МБ
                            var targetSizeBytes = 5 * 1024 * 1024; // 5 МБ для каждого видео (возвращаем к исходному размеру)
                            logger?.LogInformation($"Сжатие видео до целевого размера 5 МБ: {vid.Path}");
                            var processedVideoPath = await videoCompressionService.CompressVideoIfNeededAsync(vid.Path, targetSizeBytes);

                            if (processedVideoPath != null)
                            {
                                logger?.LogInformation($"Видео после сжатия: {processedVideoPath}");
                                
                                // Проверяем размер обработанного файла
                                if (File.Exists(processedVideoPath))
                                {
                                    var processedFileInfo = new FileInfo(processedVideoPath);
                                    logger?.LogInformation($"Размер обработанного файла: {processedFileInfo.Length} байт ({processedFileInfo.Length / (1024.0 * 1024.0):F2} МБ)");
                                    
                                    if (processedFileInfo.Length > targetSizeBytes * 1.1) // Допускаем 10% превышение
                                    {
                                        logger?.LogWarning($"Обработанный файл превышает целевой размер: {processedVideoPath}");
                                        // Даже если файл превышает размер, мы все равно пытаемся его отправить
                                        logger?.LogWarning($"Попытка отправки файла несмотря на превышение размера...");
                                    }
                                }
                                else
                                {
                                    logger?.LogWarning($"Обработанный файл не существует: {processedVideoPath}");
                                    // Даже если сжатие не удалось, пытаемся использовать оригинальный файл
                                    processedVideoPath = vid.Path;
                                    logger?.LogWarning($"Используется оригинальный файл для отправки: {processedVideoPath}");
                                }
                                
                                // Используем обработанное видео (оригинал или сжатое)
                                logger?.LogInformation($"Попытка получения потока файла: {processedVideoPath}");
                                var videoStream = await fileHelper.GetFileStreamFromVideo(processedVideoPath);
                                if (videoStream != null)
                                {
                                    logger?.LogInformation($"Поток файла успешно получен: {processedVideoPath}");
                                    media.Add(new InputMediaVideo(InputFile.FromStream(videoStream, Path.GetFileName(processedVideoPath))));
                                    groupProcessedVideos.Add(processedVideoPath);
                                    
                                    // Если это сжатое видео, отслеживаем его для последующей очистки
                                    if (processedVideoPath != vid.Path)
                                    {
                                        processedVideos.Add(processedVideoPath);
                                        logger?.LogInformation($"Добавлен временный файл для очистки: {processedVideoPath}");
                                    }
                                }
                                else
                                {
                                    logger?.LogWarning($"Не удалось получить поток файла: {processedVideoPath}");
                                    // Файл недоступен, удаляем из базы данных только если не можем получить поток
                                    if (await videoRepository.RemoveByPathAsync(vid.Path))
                                        logger?.LogWarning($"Данные удалены из базы данных (недоступен поток): {vid.Path}");
                                    
                                    // Удаляем временный сжатый файл, если он был создан и не можем использовать оригинальный файл
                                    if (processedVideoPath != vid.Path && File.Exists(processedVideoPath))
                                    {
                                        File.Delete(processedVideoPath);
                                        logger?.LogInformation($"Удален временный сжатый файл: {processedVideoPath}");
                                    }
                                }
                            }
                            else
                            {
                                logger?.LogWarning($"Не удалось обработать видео (сжатие не удалось): {vid.Path}");
                                // Даже если сжатие не удалось, пытаемся использовать оригинальный файл
                                logger?.LogWarning($"Попытка использования оригинального файла для отправки: {vid.Path}");
                                var originalVideoPath = vid.Path;
                                
                                // Проверяем оригинальный файл
                                if (File.Exists(originalVideoPath))
                                {
                                    var originalFileInfo = new FileInfo(originalVideoPath);
                                    logger?.LogInformation($"Размер оригинального файла: {originalFileInfo.Length} байт ({originalFileInfo.Length / (1024.0 * 1024.0):F2} МБ)");
                                    
                                    // Используем оригинальный файл
                                    logger?.LogInformation($"Попытка получения потока оригинального файла: {originalVideoPath}");
                                    var videoStream = await fileHelper.GetFileStreamFromVideo(originalVideoPath);
                                    if (videoStream != null)
                                    {
                                        logger?.LogInformation($"Поток оригинального файла успешно получен: {originalVideoPath}");
                                        media.Add(new InputMediaVideo(InputFile.FromStream(videoStream, Path.GetFileName(originalVideoPath))));
                                        // Не добавляем в processedVideos, так как это оригинальный файл
                                    }
                                    else
                                    {
                                        logger?.LogWarning($"Не удалось получить поток оригинального файла: {originalVideoPath}");
                                        // Файл недоступен, удаляем из базы данных
                                        if (await videoRepository.RemoveByPathAsync(vid.Path))
                                            logger?.LogWarning($"Данные удалены из базы данных (недоступен поток оригинального файла): {vid.Path}");
                                    }
                                }
                                else
                                {
                                    logger?.LogWarning($"Оригинальный файл не существует: {originalVideoPath}");
                                    // Файл не существует, удаляем из базы данных
                                    if (await videoRepository.RemoveByPathAsync(vid.Path))
                                        logger?.LogWarning($"Данные удалены из базы данных (оригинальный файл не существует): {vid.Path}");
                                }
                            }

                        }
                        catch (Exception ex)
                        {
                            logger?.LogError(ex, $"Ошибка обработки видео: {vid.Path}");
                            // Продолжаем обработку других видео в группе
                        }
                    }
                    
                    logger?.LogInformation($"Группа медиа сформирована. Количество элементов: {media.Count}");
                    
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
                logger?.LogInformation($"=== РЕЗУЛЬТАТЫ ОБРАБОТКИ ===");
                logger?.LogInformation($"Всего видео в отчете: {videoList.Count}");
                logger?.LogInformation($"Успешно отправлено: {alreadySentVideos}");
                
                if (alreadySentVideos == 0 && videoList.Count > 0)
                {
                    sendSuccess = false;
                    errorMessage = "Все видео из отчета недоступны, превышают допустимый размер или были удалены из базы данных.";
                    logger?.LogWarning($"ОШИБКА: {errorMessage}");
                    
                    // Дополнительная отладочная информация
                    logger?.LogWarning($"=== ДЕТАЛИЗАЦИЯ ПРОБЛЕМЫ ===");
                    logger?.LogWarning($"- Всего видео в отчете: {videoList.Count}");
                    logger?.LogWarning($"- Успешно отправлено: {alreadySentVideos}");
                    
                    await bot.SendMessage(
                        chatId: chatId,
                        text: $"⚠️ {errorMessage}\n\nДетали:\n- Всего видео: {videoList.Count}\n- Отправлено: {alreadySentVideos}",
                        parseMode: ParseMode.Html
                    );
                }
                else if (alreadySentVideos < videoList.Count)
                {
                    logger?.LogWarning($"Частичная отправка: {alreadySentVideos} из {videoList.Count} видео");
                    
                    await bot.SendMessage(
                        chatId: chatId,
                        text: $"ℹ️ Отправлено {alreadySentVideos} из {videoList.Count} видео. Недоступные видео или видео, превышающие допустимый размер, были удалены из базы данных.",
                        parseMode: ParseMode.Html
                    );
                }
                else
                {
                    logger?.LogInformation($"Успешная отправка всех видео: {alreadySentVideos}");
                    
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
                
                // Проверяем, есть ли уже запись для этого периода отчета (асинхронно)
                var existingReportStatus = await dbConnection.GetReportStatusAsync(start, end);
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