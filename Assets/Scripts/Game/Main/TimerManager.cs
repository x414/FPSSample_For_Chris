using System;

using UnityEngine;

public class TimerManager
{
    public float remainingTime { get; private set; }
    public bool IsExpired => remainingTime <= 0f;

    public TimerManager(float durationMinutes)
    {
        remainingTime = durationMinutes * 60f;
    }

    public void Tick(float deltaTime)
    {
        if (remainingTime > 0)
            remainingTime -= deltaTime;
    }

    public string GetFormattedTime()
    {
        var ts = TimeSpan.FromSeconds(Mathf.Max(0, remainingTime));
        return $"{ts.Minutes:D2}:{ts.Seconds:D2}";
    }
}
