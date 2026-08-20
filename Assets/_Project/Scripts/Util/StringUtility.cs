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

    // Appends a "+N" level suffix to a display name once level > 0 - e.g. "Shotgun" at Level 1 reads
    // "Shotgun +1". Shared by every place a weapon's own Weapon.Level (or a not-yet-granted Store
    // offer's own rolled WeaponLevel) needs to show up in its display name - GameplayUiController.
    // BuildWeaponCardData and CurrentWeaponUiWidget's live HUD readout - so the format can't drift
    // between the two.
    public static string WithLevelSuffix(string name, int level)
    {
        return level > 0 ? $"{name} +{level}" : name;
    }
}
