using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UI;

namespace _Project.Scripts.Util
{
    public static class GlobalExtensions
    {
        public static Color SetAlpha(this Color a, float alpha)
        {
            var newColor = new Color(a.r, a.g, a.b, alpha);
            return newColor;
        }

        public static void SetSprites(this Image[] images, Sprite sprite)
        {
            foreach (var image in images)
            {
                image.sprite = sprite;
            }
        }
        public static void SetSprites(this List<Image> images, Sprite sprite)
        {
            foreach (var image in images)
            {
                image.sprite = sprite;
            }
        }
        public static void SetColor(this Image[] images, Color color)
        {
            foreach (var image in images)
            {
                image.color = color;
            }
        }
        public static void SetColor(this List<Image> images, Color color)
        {
            foreach (var image in images)
            {
                image.color = color;
            }
        }
        public static string ToDescriptionString(this TimeSpan span, string prefix = null, bool longStyle = false, int maxParts = 2)
        {
            if (maxParts < 1) maxParts = 1;

            // Treat negative/zero as "now"
            if (span <= TimeSpan.Zero)
                return string.IsNullOrWhiteSpace(prefix) ? "now" : $"{prefix.ToSentenceCase()} now";

            var parts = new List<string>(4);

            void AddPart(int value, string shortUnit, string longSingular, string longPlural)
            {
                if (value > 0 && parts.Count < maxParts)
                {
                    if (longStyle)
                        parts.Add(value == 1 ? $"{value} {longSingular}" : $"{value} {longPlural}");
                    else
                        parts.Add($"{value}{shortUnit}");
                }
            }

            // Add units in descending order of significance
            AddPart(span.Days, "d", "day", "days");
            AddPart(span.Hours, "h", "hour", "hours");
            AddPart(span.Minutes, "m", "minute", "minutes");
            AddPart(span.Seconds, "s", "second", "seconds");

            if (parts.Count == 0)
                parts.Add(longStyle ? "1 second" : "1s");

            string body;
            if (longStyle)
                body = parts.Count == 1 ? parts[0] : $"{parts[0]} and {parts[1]}";
            else
                body = string.Join(" ", parts);

            if (string.IsNullOrWhiteSpace(prefix))
                return body;

            return $"{prefix.ToSentenceCase()} {body}";
        }

        /// <summary>Uppercases the first letter for readability.</summary>
        private static string ToSentenceCase(this string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (char.IsUpper(s[0])) return s;
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }
        public static DateTime ToDate(this string dateStr)
        {
            if (string.IsNullOrWhiteSpace(dateStr))
                throw new ArgumentException("Date string cannot be null or empty.", nameof(dateStr));

            // Define the expected format
            string format = "dd/MM/yyyy";
            CultureInfo provider = CultureInfo.InvariantCulture;

            // Try parsing the date string
            if (DateTime.TryParseExact(dateStr, format, provider, DateTimeStyles.None, out DateTime result))
            {
                return result;
            }

            throw new FormatException($"Invalid date format. Expected format is {format}, but got '{dateStr}'.");
        }
        public static Transform FindChildRecursively(this Transform parent, string childName)
        {
            if (parent.name.Contains( childName))
            {
                return parent;
            }

            foreach (Transform child in parent)
            {
                Transform result = FindChildRecursively(child, childName);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }
        public static void ChangeLayerWithChildRecursively(this GameObject gameObject, int layer)
        {
            gameObject.layer = layer;
            foreach (Transform child in gameObject.transform)
            {
                ChangeLayerWithChildRecursively(child.gameObject, layer);
            }
        }


    }
}