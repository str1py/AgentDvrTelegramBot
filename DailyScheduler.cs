using System;
using System.Threading;
using System.Threading.Tasks;
using CountryTelegramBot;

using CountryTelegramBot.Models;

public class DailyScheduler : IDailyScheduler
{
    private readonly Timer timer;
    // Можно добавить IErrorHandler для централизованной обработки ошибок

    public DailyScheduler(TimerCallback ExecuteChecks)
    {
        timer = new Timer(ExecuteChecks, null, Timeout.Infinite, Timeout.Infinite);
        StartTimer();
    }

    public void StartTimer()
    {
        var interval = TimeSpan.FromMinutes(1);
        timer.Change(0, (int)interval.TotalMilliseconds);
    }

    public void Dispose()
    {
        timer?.Dispose();
    }
}
