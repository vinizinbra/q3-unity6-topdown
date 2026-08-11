using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace QuantumUser.View.Util
{
    // Wraps Debug.Log/LogWarning/LogError with a `[Tag]` colored by category so related lines
    // (Director/Experience/LevelUp/Perks/Burrow/...) stand out from each other in a bloated console.
    // Category colors are stable and hashed from the tag string, so a new tag never has to be
    // registered anywhere - it just gets a consistent color the first time it's logged.
    public static class LogHelper
    {
        // Reserve well-known tags to a fixed color instead of a hashed one, so the systems
        // called out in CLAUDE.md (Director/Experience/LevelUp/Perks/Burrow) stay recognizable.
        private static readonly (string Tag, string Hex)[] PinnedColors =
        {
            ("Director", "4FC3F7"),
            ("Experience", "AED581"),
            ("LevelUp", "FFD54F"),
            ("Perk", "BA68C8"),
            ("Burrow", "FF8A65"),
        };

        // Distinct, readable-on-dark-and-light-console hues for hashed (non-pinned) tags.
        private static readonly string[] PaletteHex =
        {
            "4FC3F7", "AED581", "FFD54F", "BA68C8", "FF8A65",
            "4DD0E1", "F06292", "81C784", "FFB74D", "9575CD",
            "64B5F6", "DCE775",
        };

        // Runtime kill switch for Editor/Development Build sessions, where the [Conditional]
        // attributes below don't strip anything (UNITY_EDITOR/DEVELOPMENT_BUILD are defined).
        // Exposed as a toggle via SROptions (see LogHelper.SROptions.cs) so it can be flipped
        // without a recompile while profiling. Has no effect on Release builds, which already
        // strip Log/Warn (and their string formatting) entirely via [Conditional].
        public static bool Disabled;

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Log(string tag, string message, Object context = null)
        {
            return;
            if (Disabled) return;
            Debug.Log(Format(tag, message), context);
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Warn(string tag, string message, Object context = null)
        {
            if (Disabled) return;
            Debug.LogWarning(Format(tag, message), context);
        }

        public static void Error(string tag, string message, Object context = null)
        {
            if (Disabled) return;
            Debug.LogError(Format(tag, message), context);
        }

        private static string Format(string tag, string message)
        {
            return $"<color=#{ColorFor(tag)}><b>[{tag}]</b></color> {message}";
        }

        private static string ColorFor(string tag)
        {
            foreach (var (pinnedTag, hex) in PinnedColors)
            {
                if (tag == pinnedTag)
                    return hex;
            }

            var index = (uint)tag.GetHashCode() % (uint)PaletteHex.Length;
            return PaletteHex[index];
        }
    }
}
