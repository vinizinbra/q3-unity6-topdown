using System;
using System.Text;

public static class StringUtility
{
    // Converts an identifier/asset-name-style string into human-readable "Title Case With Spaces" -
    // e.g. Beautify("AssaultRifleWeaponData", "WeaponData") and Beautify("assault_rifle") both give
    // "Assault Rifle". Handles PascalCase/camelCase word boundaries and underscores; stripSuffix (if
    // given) is removed first, case-insensitively. Used wherever a human-facing name has to fall back
    // to an asset's own file name (e.g. GameplayUiController.BuildWeaponCardData, when
    // WeaponDataAsset.DisplayName hasn't been authored).
    public static string Beautify(string raw, string stripSuffix = null)
    {
        if (string.IsNullOrEmpty(raw))
            return raw;

        if (string.IsNullOrEmpty(stripSuffix) == false
            && raw.Length > stripSuffix.Length
            && raw.EndsWith(stripSuffix, StringComparison.OrdinalIgnoreCase))
        {
            raw = raw.Substring(0, raw.Length - stripSuffix.Length);
        }

        raw = raw.Replace('_', ' ');

        var spaced = new StringBuilder(raw.Length + 8);

        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];

            // Word boundary: an uppercase letter right after a lowercase letter or digit, e.g. the
            // "R" in "assaultRifle" or "Rifle2" - insert a split there.
            if (i > 0 && char.IsUpper(c) && (char.IsLower(raw[i - 1]) || char.IsDigit(raw[i - 1])))
                spaced.Append(' ');

            spaced.Append(c);
        }

        string[] words = spaced.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < words.Length; i++)
        {
            string word = words[i];
            words[i] = word.Length == 1
                ? word.ToUpperInvariant()
                : char.ToUpperInvariant(word[0]) + word.Substring(1).ToLowerInvariant();
        }

        return string.Join(" ", words);
    }

    // Appends a " - LvN" level suffix to a display name once level > 0 - e.g. "Shotgun" at Level 1
    // reads "Shotgun - Lv1". Shared by every place a weapon's own Weapon.Level (or a not-yet-granted
    // Store offer's own rolled WeaponLevel) needs to show up in its display name - GameplayUiController.
    // BuildWeaponCardData and CurrentWeaponUiWidget's live HUD readout - so the format can't drift
    // between the two.
    public static string WithLevelSuffix(string name, int level)
    {
        return level > 0 ? $"{name} - Lv{level}" : name;
    }

    // Converts a positive rank/level into a Roman numeral - "II", "III", "IV". Standard greedy
    // subtraction, so it's correct for any positive number, not just the 1-3 every current Ascension
    // line happens to cap at. Returns an empty string for 0 or less ("no rank"). Lives here next to
    // WithLevelSuffix for the same reason: every place a rank shows up in a display name shares one
    // implementation instead of a per-widget private copy (UpgradeCardWidget's level-up card,
    // UpgradeWidget's popup row, DebugUpgradeButtonWidget's debug row).
    public static string ToRomanNumeral(int number)
    {
        if (number <= 0)
            return string.Empty;

        (int value, string symbol)[] map =
        {
            (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"),
            (100, "C"), (90, "XC"), (50, "L"), (40, "XL"),
            (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")
        };

        var builder = new StringBuilder();

        foreach ((int value, string symbol) in map)
        {
            while (number >= value)
            {
                builder.Append(symbol);
                number -= value;
            }
        }

        return builder.ToString();
    }

    // Appends a " - <Roman numeral>" rank suffix to a display name once rank > 0 - e.g. "Glass Core"
    // at rank 2 reads "Glass Core - II". The rank counterpart of WithLevelSuffix, and the one format
    // every ranked display name goes through so the level-up card and the hero-info popup row can't
    // drift apart.
    public static string WithRankSuffix(string name, int rank)
    {
        return rank > 0 ? $"{name} - {ToRomanNumeral(rank)}" : name;
    }
}
