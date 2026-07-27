using System;
using UnityEngine;

public static class PgFloatExtensions
{
    public static string ToLapTime(this float totalSeconds)
    {
        int minutes = (int)(totalSeconds / 60f);
        float seconds = totalSeconds % 60f;

        return $"{minutes:0}:{seconds:00.000}";
    }
    public static string SecondsToShort(this double s) {
        if (s < 0) s = 0;
        var ts = TimeSpan.FromSeconds(s);
        if (ts.TotalDays >= 1) return $"{(int)ts.TotalDays}d {(int)ts.Hours}h";
        if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h {(int)ts.Minutes}m";
        if (ts.TotalMinutes >= 1) return $"{(int)ts.Minutes}m {(int)ts.Seconds}s";
        return $"{(int)ts.Seconds}s";
    }

}