using System;
using System.Threading;
using System.Threading.Tasks;
using CountryTelegramBot;

public class DailyScheduler : IDisposable
{
    private readonly Timer timer;

    public DailyScheduler(TimerCallback ExecuteChecks)
    {
        // Инициализация таймера
        timer = new Timer(ExecuteChecks, null, Timeout.Infinite, Timeout.Infinite);       
        // Запуск периодических проверок
        StartTimer();
    }

    private void StartTimer()
    {
        // Пересчет интервала (каждую минуту)
        var interval = TimeSpan.FromMinutes(1);
        timer.Change(0, (int)interval.TotalMilliseconds);
    }

    public void Dispose()
    {
        timer?.Dispose();
    }
}
