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
        private string agentDvrUrl;
        private string agentDvrCredentials;
        private string agentUser;
        private string agentPass;
        private readonly ILogger? logger;
        private bool IsForcedArmedAtNight;
        private bool IsForcedArmedAtDay;
        private TimeHelper timeHelper;
        private DailyScheduler dailyScheduler;
        public AgentDVR(string agentDvrUrl, string user, string password, IConfigurationSection config, ILogger? logger)
        {
            this.agentDvrUrl = agentDvrUrl;
            agentUser = user;
            agentPass = password;
            this.logger = logger;

            IsForcedArmedAtNight = bool.Parse(config["ForcedArmedAtNight"] ?? "false");
            IsForcedArmedAtDay = bool.Parse(config["ForcedArmedAtDay"] ?? "false");

            timeHelper = new TimeHelper(logger);

            httpClient = new HttpClient();
            agentDvrCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{agentUser}:{agentPass}"));
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", agentDvrCredentials);
            _ = InitSystemState();
            dailyScheduler = new DailyScheduler(ForcedArmedByTime);
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
        public async Task SetArmState(bool armed)
        {
            try
            {
                string command = armed ? "arm" : "disarm";
                var response = await httpClient.GetAsync($"{agentDvrUrl}/command/{command}");
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Ошибка изменения состояния охраны");
                throw;
            }
        }

        private async void ForcedArmedByTime(object state)
        {
            var now = DateTime.Now;
            logger?.LogInformation($"{DateTime.Now.ToShortTimeString()}: Here is a tick in ForecedArmedByTime");
            if (await GetSystemState())
            {
                var isArmed = await GetArmState();

                //Дневная c 8:00 по 23:00
                var minDay = now.Date.Add(timeHelper.forcedArmedDayTime);
                var maxDay = now.Date.Add(timeHelper.forcedArmedNightTime);
                if (now.Hour >= minDay.Hour && now.Hour < maxDay.Hour)
                {
                    //logger?.LogInformation("Сейчас идет дневной цикл");
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

                //Ночная с 23:00 до 8:00 следующего дня
                var minNight = now.Date.Add(timeHelper.forcedArmedNightTime);
                var maxNight = now.Date.Add(timeHelper.forcedArmedDayTime).AddDays(1);
                if (now.Hour >= minNight.Hour && now <= maxNight)
                {
                    //logger?.LogInformation("Сейчас идет ночной цикл");
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

        public async Task RebootDVR()
        {
            try
            {
                var response = await httpClient.GetAsync($"{agentDvrUrl}/command/restart");
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Ошибка изменения состояния охраны");
                throw;
            }
        }

        public async Task<bool> GetArmState()
        {
            try
            {
                var response = await httpClient.GetAsync($"{agentDvrUrl}/command/getStatus");
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                // Парсим ответ, ищем статус охраны
                return content.Contains("\"armed\":true");
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Ошибка получения статуса охраны");
                return false;
            }
        }
        public async Task<string> GetArmStateMessage()
        {
            var status = await GetArmState();
            return status ? "✅Охрана активна" : "⛔ Охрана неактивна";
        }
        public async Task<bool> GetSystemState()
        {
            var response = await httpClient.GetAsync($"{agentDvrUrl}/command/ping");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();

            if (content.Trim() == "{\"status\":\"ok\"}")
            {
                return true;
            } else return false;
        }
    }
}

