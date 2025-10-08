using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CountryTelegramBot;

using CountryTelegramBot.Models;

public class DailyScheduler : IDailyScheduler
{
    private Timer timer;
    private List<TimerCallback> callbacks;
    // Можно добавить IErrorHandler для централизованной обработки ошибок

    public DailyScheduler(TimerCallback ExecuteChecks)
    {
        callbacks = new List<TimerCallback> { ExecuteChecks };
        timer = new Timer(ExecuteAllCallbacks, null, Timeout.Infinite, Timeout.Infinite);
        StartTimer();
    }
    
    public DailyScheduler(TimerCallback[] ExecuteChecks)
    {
        callbacks = new List<TimerCallback>(ExecuteChecks);
        timer = new Timer(ExecuteAllCallbacks, null, Timeout.Infinite, Timeout.Infinite);
        StartTimer();
    }

    private async void ExecuteAllCallbacks(object? state)
    {
        foreach (var callback in callbacks)
        {
            try
            {
                callback(state);
            }
            catch (Exception ex)
            {
                // Логируем исключение, но продолжаем выполнение других callback-функций
                Console.WriteLine($"Ошибка в callback-функции DailyScheduler: {ex.Message}");
            }
        }
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