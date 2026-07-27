using System.Globalization;
using System.Text.RegularExpressions;

public static class PgStringExtensions
{
    
    public static string SplitCamelCase(this string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        return Regex.Replace(input, "(?<=[a-z])([A-Z])", " $1");
    }
    
    /// <summary>
    /// Converts a string like "test_emoji", "testEmoji", "test__emoji" or "test emoji"
    /// into a display name like "Test Emoji", removing optional prefix or suffix.
    /// </summary>
    public static string ToDisplayName(this string input, string removePrefix = null, string removeSuffix = null)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        string s = input.Trim();

        // Replace underscores/double underscores with spaces
        s = Regex.Replace(s, @"_+", " ");

        // Insert spaces before capital letters (for camelCase)
        s = Regex.Replace(s, @"(?<=[a-z])([A-Z])", " $1");

        // Normalize multiple spaces
        s = Regex.Replace(s, @"\s+", " ");

        s = s.Trim();

        // Convert to lowercase for matching prefix/suffix safely
        string lower = s.ToLowerInvariant();

        // Try removing prefix
        if (!string.IsNullOrWhiteSpace(removePrefix))
        {
            string prefix = removePrefix.Trim().ToLowerInvariant();
            if (lower.StartsWith(prefix + " "))
            {
                s = s.Substring(prefix.Length + 1);
                lower = s.ToLowerInvariant();
            }
            else if (lower.StartsWith(prefix))
            {
                s = s.Substring(prefix.Length);
                lower = s.ToLowerInvariant();
            }
        }

        // Try removing suffix
        if (!string.IsNullOrWhiteSpace(removeSuffix))
        {
            string suffix = removeSuffix.Trim().ToLowerInvariant();
            if (lower.EndsWith(" " + suffix))
            {
                s = s.Substring(0, s.Length - (suffix.Length + 1));
            }
            else if (lower.EndsWith(suffix))
            {
                s = s.Substring(0, s.Length - suffix.Length);
            }
        }

        // Clean leftover underscores/spaces again
        s = Regex.Replace(s, @"[_\s]+", " ").Trim();

        // Title case the result
        TextInfo textInfo = CultureInfo.InvariantCulture.TextInfo;
        s = textInfo.ToTitleCase(s.ToLowerInvariant());

        return s;
    }
    public static string RemoveSpaces(this string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        return input.Replace(" ", string.Empty);
    }

    public static string NormalizeId(this string raw,string defaultString = "")
    {
        if (string.IsNullOrWhiteSpace(raw)) return defaultString;
        // Replace whitespace with underscores
        string s = System.Text.RegularExpressions.Regex.Replace(raw.Trim(), @"\s+", "_");
        // Remove characters that are not letters, digits, or underscore
        s = System.Text.RegularExpressions.Regex.Replace(s, @"[^A-Za-z0-9_]", "_");
        // Collapse multiple underscores
        s = System.Text.RegularExpressions.Regex.Replace(s, @"_+", "_");
        // Trim underscores at edges
        s = s.Trim('_');
        return string.IsNullOrEmpty(s) ? defaultString : s;
    }
}
