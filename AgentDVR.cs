using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;
using Microsoft.VisualBasic;

namespace CountryTelegramBot
{
    public class AgentDVR
    {
        private readonly HttpClient httpClient;
        private readonly string agentDvrUrl;
        private string? agentDvrCredentials;
        private readonly string agentUser;
        private readonly string agentPass;
        private readonly ILogger? logger;
    private readonly bool IsForcedArmedAtNight;
    private readonly bool IsForcedArmedAtDay;
    private readonly TimeHelper timeHelper;
    private readonly DailyScheduler dailyScheduler;
        // Команды и endpoint'ы
        private const string CommandArm = "/command/arm";
        private const string CommandDisarm = "/command/disarm";
        private const string CommandRestart = "/command/restart";
        private const string CommandGetStatus = "/command/getStatus";
        private const string CommandPing = "/command/ping";

        /// <summary>
        /// Создаёт экземпляр класса AgentDVR для управления DVR через HTTP API.
        /// </summary>
        /// <param name="agentDvrUrl">Базовый URL AgentDVR</param>
        /// <param name="user">Имя пользователя DVR</param>
        /// <param name="password">Пароль DVR</param>
        /// <param name="config">Конфигурация общих параметров</param>
        /// <param name="logger">Логгер</param>
        /// <summary>
        /// Создаёт экземпляр класса AgentDVR для управления DVR через HTTP API.
        /// </summary>
        /// <param name="agentDvrUrl">Базовый URL AgentDVR</param>
        /// <param name="user">Имя пользователя DVR</param>
        /// <param name="password">Пароль DVR</param>
        /// <param name="config">Конфигурация общих параметров</param>
        /// <param name="logger">Логгер</param>
        /// <param name="httpClient">Экземпляр HttpClient (желательно внедрять через DI)</param>
        public AgentDVR(string agentDvrUrl, string user, string password, Configs.CommonConfig config, ILogger? logger, HttpClient httpClient)
        {
            if (string.IsNullOrWhiteSpace(agentDvrUrl))
                throw new ArgumentException("agentDvrUrl не может быть пустым", nameof(agentDvrUrl));
            if (string.IsNullOrWhiteSpace(user))
                throw new ArgumentException("user не может быть пустым", nameof(user));
            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("password не может быть пустым", nameof(password));
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            if (httpClient == null)
                throw new ArgumentNullException(nameof(httpClient));

            this.agentDvrUrl = agentDvrUrl;
            agentUser = user;
            agentPass = password;
            this.logger = logger;
            this.httpClient = httpClient;
            IsForcedArmedAtNight = config.ForcedArmedAtNight;
            IsForcedArmedAtDay = config.ForcedArmedAtDay;
            timeHelper = new TimeHelper(logger);
            SetAuthorizationHeader();
            dailyScheduler = new DailyScheduler(ForcedArmedByTime);
            LogAgentDvrCreated();
        }
        private string MaskSecret(string secret)
        {
            if (string.IsNullOrEmpty(secret)) return "[empty]";
            if (secret.Length <= 4) return "****";
            return secret.Substring(0, 2) + new string('*', secret.Length - 4) + secret.Substring(secret.Length - 2);
        }
        /// <summary>
        /// Асинхронная инициализация состояния системы. Вызывать явно после создания объекта.
        /// </summary>
        public async Task InitializeAsync()
        {
            try
            {
                await InitSystemState();
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Ошибка инициализации AgentDVR");
            }
        }

        // Старый конструктор для обратной совместимости (создаёт новый HttpClient)
        private void LogAgentDvrCreated()
        {
            logger?.LogInformation($"AgentDVR создан. URL: {agentDvrUrl}, User: {MaskSecret(agentUser)}");
        }

        private string MaskSecret(string secret)
        {
            if (string.IsNullOrEmpty(secret)) return "[empty]";
            if (secret.Length <= 4) return "****";
            return secret.Substring(0, 2) + new string('*', secret.Length - 4) + secret.Substring(secret.Length - 2);
        }
        public AgentDVR(string agentDvrUrl, string user, string password, Configs.CommonConfig config, ILogger? logger)
            : this(agentDvrUrl, user, password, config, logger, new HttpClient())
        {
        }

        /// <summary>
        /// Устанавливает заголовок авторизации для httpClient.
        /// </summary>
        public void SetAuthorizationHeader()
        {
            agentDvrCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{agentUser}:{agentPass}"));
            if (agentDvrCredentials != null)
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", agentDvrCredentials);
        }

        

        private async Task InitSystemState()
        {
            if (await GetSystemState())
                logger?.LogInformation("AgentDVR: Система активна");
            else logger?.LogInformation("AgentDVR: Система НЕ активна");

            if (await GetArmState())
                logger?.LogInformation("AgentDVR: Защита активна");
            else logger?.LogInformation("AgentDVR: Защита НЕ активна");
        }
        /// <summary>
        /// Включает или отключает охрану.
        /// </summary>
        /// <param name="armed">true — включить охрану, false — отключить</param>
        public async Task SetArmState(bool armed)
        {
            string endpoint = armed ? CommandArm : CommandDisarm;
            var url = $"{agentDvrUrl}{endpoint}";
            try
            {
                var response = await httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    logger?.LogError($"Ошибка изменения состояния охраны. URL: {url}, Status: {response.StatusCode}, Content: {content}");
                }
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, $"Ошибка изменения состояния охраны. URL: {url}");
                throw;
            }
        }

        private async void ForcedArmedByTime(object? state)
        {
            var now = DateTime.Now;
            logger?.LogInformation($"{DateTime.Now.ToShortTimeString()}: Here is a tick in ForecedArmedByTime");
            if (await GetSystemState())
            {
                var isArmed = await GetArmState();

                (DateTime minDay, DateTime maxDay) = GetDayInterval(now);
                if (now.Hour >= minDay.Hour && now.Hour < maxDay.Hour)
                {
                    if (IsForcedArmedAtDay && !isArmed)
                    {
                        await SetArmState(true);
                        logger?.LogInformation($"Включаю дневную защиту до {maxDay}");
                    }
                    else if (IsForcedArmedAtDay && isArmed)
                        logger?.LogInformation($"Дневная защита уже включена до {maxDay}");
                    else if (!IsForcedArmedAtDay && isArmed)
                    {
                        logger?.LogInformation($"Произвожу отключение защиты (дневная защита отключена в настройках)");
                        await SetArmState(false);
                    }
                }

                (DateTime minNight, DateTime maxNight) = GetNightInterval(now);
                if (now.Hour >= minNight.Hour && now <= maxNight)
                {
                    if (IsForcedArmedAtNight && !isArmed)
                    {
                        await SetArmState(true);
                        logger?.LogInformation($"Включаю ночную защиту до {maxNight}");
                    }
                    else if (IsForcedArmedAtNight && isArmed)
                        logger?.LogInformation($"Ночная защита уже включена до {maxNight}");
                    else if (!IsForcedArmedAtNight && isArmed)
                    {
                        logger?.LogInformation($"Произвожу отключение защиты (ночная защита отключена в настройках)");
                        await SetArmState(false);
                    }
                }
            }
        }

        /// <summary>
        /// Возвращает дневной временной интервал (minDay, maxDay) для текущей даты
        /// </summary>
        private (DateTime minDay, DateTime maxDay) GetDayInterval(DateTime now)
        {
            var minDay = now.Date.Add(timeHelper.ForcedArmedDayTime);
            var maxDay = now.Date.Add(timeHelper.ForcedArmedNightTime);
            return (minDay, maxDay);
        }

        /// <summary>
        /// Возвращает ночной временной интервал (minNight, maxNight) для текущей даты
        /// </summary>
        private (DateTime minNight, DateTime maxNight) GetNightInterval(DateTime now)
        {
            var minNight = now.Date.Add(timeHelper.ForcedArmedNightTime);
            var maxNight = now.Date.Add(timeHelper.ForcedArmedDayTime).AddDays(1);
            return (minNight, maxNight);
        }

        /// <summary>
        /// Перезагружает DVR через API.
        /// </summary>
        public async Task RebootDVR()
        {
            var url = $"{agentDvrUrl}{CommandRestart}";
            try
            {
                var response = await httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    logger?.LogError($"Ошибка перезагрузки DVR. URL: {url}, Status: {response.StatusCode}, Content: {content}");
                }
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, $"Ошибка перезагрузки DVR. URL: {url}");
                throw;
            }
        }

        /// <summary>
        /// Получает статус охраны (включена/выключена).
        /// </summary>
        /// <returns>true — охрана включена, false — выключена или ошибка</returns>
        public async Task<bool> GetArmState()
        {
            var url = $"{agentDvrUrl}{CommandGetStatus}";
            try
            {
                var response = await httpClient.GetAsync(url);
                var content = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    logger?.LogError($"Ошибка получения статуса охраны. URL: {url}, Status: {response.StatusCode}, Content: {content}");
                    return false;
                }
                // Парсим ответ, ищем статус охраны
                return content.Contains("\"armed\":true");
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, $"Ошибка получения статуса охраны. URL: {url}");
                return false;
            }
        }
        /// <summary>
        /// Возвращает текстовое сообщение о статусе охраны для пользователя.
        /// </summary>
        /// <returns>Строка с иконкой и статусом</returns>
        public async Task<string> GetArmStateMessage()
        {
            var status = await GetArmState();
            return status ? "✅Охрана активна" : "⛔ Охрана неактивна";
        }
        /// <summary>
        /// Проверяет доступность системы DVR (пинг).
        /// </summary>
        /// <returns>true — система доступна, false — нет</returns>
        public async Task<bool> GetSystemState()
        {
            var url = $"{agentDvrUrl}{CommandPing}";
            try
            {
                var response = await httpClient.GetAsync(url);
                var content = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    logger?.LogError($"Ошибка получения статуса системы. URL: {url}, Status: {response.StatusCode}, Content: {content}");
                    return false;
                }
                if (content.Trim() == "{\"status\":\"ok\"}")
                {
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, $"Ошибка получения статуса системы. URL: {url}");
                return false;
            }
        }
    }
}

