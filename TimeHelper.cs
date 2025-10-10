using Microsoft.Extensions.Logging;

namespace CountryTelegramBot
{
    using CountryTelegramBot.Models;

    public class TimeHelper : ITimeHelper
    {
        public DateTime MorningReport 
        { 
            get 
            { 
                var now = DateTime.Now;
                // Утренний отчет должен отправляться сегодня в 8:00, если сейчас уже прошло 8:00
                // Иначе завтра в 8:00
                return now.TimeOfDay >= new TimeSpan(8, 00, 0) 
                    ? now.Date.Add(new TimeSpan(8, 00, 0)) 
                    : now.Date.AddDays(1).Add(new TimeSpan(8, 00, 0));
            } 
        }
        
        public DateTime EveningReport 
        { 
            get 
            { 
                var now = DateTime.Now;
                var today8am = now.Date.Add(new TimeSpan(8, 00, 0));
                var today9pm = now.Date.Add(new TimeSpan(23, 0, 0));
                var tomorrow8am = now.Date.AddDays(1).Add(new TimeSpan(8, 00, 0));
                return now < today8am ? today8am :
                            now < today9pm ? today9pm :
                            tomorrow8am;
            } 
        }
        
        public DateTime NightVideoStartDate 
        { 
            get 
            { 
                var now = DateTime.Now;
                return now.Date.Add(new TimeSpan(23, 0, 0));
            } 
        }
        
        public DateTime NightVideoEndDate 
        { 
            get 
            { 
                var now = DateTime.Now;
                return now.Date.AddDays(1).Add(new TimeSpan(8, 00, 0));
            } 
        }
        
        public DateTime DayVideoStartDate 
        { 
            get 
            { 
                var now = DateTime.Now;
                return now.Date.Add(new TimeSpan(8, 00, 0));
            } 
        }
        
        public DateTime DayVideoEndDate 
        { 
            get 
            { 
                var now = DateTime.Now;
                return now.Date.Add(new TimeSpan(23, 0, 0));
            } 
        }

        public TimeSpan ForcedArmedNightTime { get; } = new TimeSpan(23, 0, 0); // 23:00
        public TimeSpan ForcedArmedDayTime { get; } = new TimeSpan(8, 0, 0);   // 8:00;

        private readonly ILogger? logger;
        private readonly IErrorHandler? errorHandler;

        public TimeHelper(ILogger? logger, IErrorHandler? errorHandler = null)
        {
            this.logger = logger;
            this.errorHandler = errorHandler;
            // Не храним даты в полях, а вычисляем их при каждом обращении
        }


    }
}