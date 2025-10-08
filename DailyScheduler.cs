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
                // Log the exception but continue with other callbacks
                Console.WriteLine($"Error in DailyScheduler callback: {ex.Message}");
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