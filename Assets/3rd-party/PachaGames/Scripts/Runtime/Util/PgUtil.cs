using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AssetKits.ParticleImage;
using PrimeTween;
using UnityEngine;
using UnityEngine.UI;

public class PgUtil : MonoBehaviour
{
    public static void ScrollToItem(RectTransform target, ScrollRect scrollRect,RectTransform contentPanel, float duration = 0)
    {
        Canvas.ForceUpdateCanvases();
        var initialPosition = contentPanel.anchoredPosition;
        var finalPosition = (Vector2)scrollRect.transform.InverseTransformPoint(contentPanel.position)
                            - (Vector2)scrollRect.transform.InverseTransformPoint(target.position) +
                            Vector2.up*(scrollRect.viewport.rect.height * 0.5f);
        if (duration == 0)
        {
            contentPanel.anchoredPosition = finalPosition;
        }
        else
            Tween.Custom(initialPosition,finalPosition,duration,(x)=> contentPanel.anchoredPosition = x,Ease.OutBack);
    }

    public static float TimeRemaining(string targetTime)
    {
        if (DateTime.TryParse(targetTime, out var next))
            return (float)(next - DateTime.Now).TotalSeconds;
        return 0;
    }
    public static bool IsNewDateNextWeek(DateTime date, DateTime newDate)
    {
        // Use ISO 8601 week rules (Monday as first day of week)
        var calendar = CultureInfo.InvariantCulture.Calendar;
        var dRule = CalendarWeekRule.FirstFourDayWeek;
        var firstDay = DayOfWeek.Monday;

        int week = calendar.GetWeekOfYear(date, dRule, firstDay);
        int newWeek = calendar.GetWeekOfYear(newDate, dRule, firstDay);

        // Handle year crossover
        int yearDiff = newDate.Year - date.Year;
        if (yearDiff == 0)
            return newWeek == week + 1;
        else if (yearDiff == 1 && newWeek == 1 && week >= 52)
            return true;

        return false;
    }
    public static double TimeTillNextWeek(DateTime date)
    {
        // ISO weeks start on Monday
        int daysUntilNextMonday = ((int)DayOfWeek.Monday - (int)date.DayOfWeek + 7) % 7;
        if (daysUntilNextMonday == 0) daysUntilNextMonday = 7; // if it's already Monday, go to next

        DateTime nextWeekStart = date.Date.AddDays(daysUntilNextMonday);
        TimeSpan remaining = nextWeekStart - date;
        return (float)remaining.TotalSeconds;
    }

    public static double TimeTillNextMonth(DateTime date)
    {
        int year = date.Year;
        int month = date.Month;

        if (month == 12)
        {
            year++;
            month = 1;
        }
        else
        {
            month++;
        }

        DateTime nextMonthStart = new DateTime(year, month, 1, 0, 0, 0, date.Kind);
        TimeSpan remaining = nextMonthStart - date;
        return (float)remaining.TotalSeconds;
    }
    public static bool IsNewDateNextMonth(DateTime date, DateTime newDate)
    {
        // Same year
        if (date.Year == newDate.Year)
            return newDate.Month == date.Month + 1;

        // Handle December -> January crossover
        if (newDate.Year == date.Year + 1 && date.Month == 12 && newDate.Month == 1)
            return true;

        return false;
    }
    public static IEnumerator SmoothScrollToItem(RectTransform target, ScrollRect scrollRect, RectTransform contentPanel, float duration = 0.3f)
    {
        Canvas.ForceUpdateCanvases();

        Vector2 startPos = contentPanel.anchoredPosition;

        Vector2 viewportLocalPosition = (Vector2)scrollRect.viewport.transform.InverseTransformPoint(scrollRect.viewport.position);
        Vector2 childLocalPosition = (Vector2)scrollRect.viewport.transform.InverseTransformPoint(target.position);

        Vector2 targetPos = startPos + (viewportLocalPosition - childLocalPosition);

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / duration;
            contentPanel.anchoredPosition = Vector2.Lerp(startPos, targetPos, t);
            yield return null;
        }

        contentPanel.anchoredPosition = targetPos;
    }
    public static string GetTimeUntilMidnightFormatted()
    {
        DateTime now = DateTime.UtcNow;
        DateTime nextMidnight = now.Date.AddDays(1);
        TimeSpan timeLeft = nextMidnight - now;

        return $"{timeLeft.Hours}h {timeLeft.Minutes}m {timeLeft.Seconds}s";
    }
    public static string GetTimeUntilEndOfDay(DateTime assignedDate, int fields = 3)
    {
        DateTime now = DateTime.Now;
        DateTime endOfDay = new DateTime(assignedDate.Date.Year,assignedDate.Month,assignedDate.Day).AddDays(1); // midnight of the next day
        
        if (now >= endOfDay)
            return "0h 0m 0s";

        TimeSpan timeLeft = endOfDay - now;
        if (fields == 2)
        {
            if (timeLeft.Hours > 0)
            {
                return $"{timeLeft.Hours}h {timeLeft.Minutes}m";
            }
            return $"{timeLeft.Minutes}m {timeLeft.Seconds}s";
        }
        return $"{timeLeft.Hours}h {timeLeft.Minutes}m {timeLeft.Seconds}s";
    }
    public static int GetTimeUntilEndOfDayInSeconds(DateTime assignedDate)
    {
        DateTime now = DateTime.Now;
        DateTime endOfDay = new DateTime(assignedDate.Date.Year,assignedDate.Month,assignedDate.Day).AddDays(1); // midnight of the next day
        

        TimeSpan timeLeft = endOfDay - now;
        return (int)timeLeft.TotalSeconds;
    }
    public int CalculateSkipCostInGems(int seconds)
    {
        float baseMinutes = seconds / 60f;
        float cost = Mathf.Pow(baseMinutes / 5f, 0.85f); // 0.85 softens exponential growth
        return Mathf.CeilToInt(cost);
    }
    public static string FormatTimeFromSeconds(double totalSecondsDouble, int fields = 3)
    {
        var totalSeconds = (int)totalSecondsDouble;
        if (totalSeconds <= 0) return "0s";

        int days = totalSeconds / 86400;
        int hours = (totalSeconds % 86400) / 3600;
        int minutes = (totalSeconds % 3600) / 60;
        int seconds = totalSeconds % 60;

        // fields 1: all non-zero units, no padding (e.g. "2h 30m 5s")
        if (fields == 1)
        {
            var s = "";
            if (days > 0) s += $"{days}d ";
            if (hours > 0) s += $"{hours}h ";
            if (minutes > 0) s += $"{minutes}m ";
            if (seconds > 0) s += $"{seconds}s";
            return s.TrimEnd();
        }
        // fields 2: top 2 relevant units (e.g. "2h 05m" or "3m 12s")
        if (fields == 2)
        {
            if (days > 0) return $"{days}d {hours:D2}h";
            if (hours > 0) return $"{hours}h {minutes:D2}m";
            return $"{minutes}m {seconds:D2}s";
        }
        // fields 3: top 3 relevant units, first unit unpadded (e.g. "2h 05m 12s", "5m 20s")
        if (days > 0) return $"{days}d {hours:D2}h {minutes:D2}m";
        if (hours > 0) return $"{hours}h {minutes:D2}m {seconds:D2}s";
        return $"{minutes}m {seconds:D2}s";
    }


    public static void CopyParticleSystemToParticleImage(ParticleSystem ps, ParticleImage pi)
    {
        var main = ps.main;
        pi.duration = main.duration;
        pi.loop = main.loop;
        pi.startColor = main.startColor;
        pi.lifetime = main.startLifetime;
        pi.gravityEnabled = main.gravityModifier.constant != 0;
        pi.gravity = new ParticleSystem.MinMaxCurve(-main.gravityModifier.constant * 2f);

        var emission = ps.emission;
        pi.rateOverTime = (emission.rateOverTime.constant + emission.rateOverDistance.constant * 10f) * 0.5f;

        var colorOverLifetime = ps.colorOverLifetime;
        if (colorOverLifetime.enabled)
        {
            pi.colorOverLifetime = colorOverLifetime.color;
        }

        var velocityOverLifetime = ps.velocityOverLifetime;
        if (velocityOverLifetime.enabled)
        {
            pi.velocityEnabled = true;
            pi.speedOverLifetime = velocityOverLifetime.speedModifier;
        }

        var noise = ps.noise;
        if (noise.enabled)
        {
            pi.noiseEnabled = true;
            pi.noiseFrequency = noise.frequency;
            pi.noiseStrength = noise.strength.constant;
        }

        var trails = ps.trails;
        if (trails.enabled)
        {
            pi.trailsEnabled = true;
            pi.trailLifetime = trails.lifetime.constant;
            pi.trailWidth = trails.widthOverTrail;
            pi.inheritParticleColor = trails.inheritParticleColor;
            pi.dieWithParticle = trails.dieWithParticles;
        }

        var psMaterial = ps.GetComponent<ParticleSystemRenderer>().sharedMaterial;
        if (psMaterial != null)
        {
            var tex = psMaterial.mainTexture as Texture2D;
            if (tex != null)
            {
                pi.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            }

            if (IsAdditiveBlend(psMaterial))
            {
                pi.material = new Material(Shader.Find("Sprites/SpriteAdd"));
            }
            else
            {
                pi.material = new Material(Shader.Find("Sprites/Default"));
            }
        }
        else
        {
            pi.material = new Material(Shader.Find("Sprites/Default"));
        }
    }

    private static bool IsAdditiveBlend(Material mat)
    {
        if (!mat.HasProperty("_DstBlend"))
            return false;
        return (int)mat.GetFloat("_DstBlend") == (int)UnityEngine.Rendering.BlendMode.One;
    }

    public static string BuildPayload(Dictionary<string, object> data)
    {
        return Newtonsoft.Json.JsonConvert.SerializeObject(data);
    }
    public static string ComputeHmac(string payload, string key)
    {
        var keyBytes = Encoding.UTF8.GetBytes(key);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(payloadBytes);
        return Convert.ToBase64String(hash);
    }
}
