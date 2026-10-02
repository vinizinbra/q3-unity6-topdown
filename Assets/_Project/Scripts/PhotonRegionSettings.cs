using System;
using Photon.Realtime;
using Playtime.Core;
using QuantumUser.View.Util;

// The player's Photon region choice, persisted. Empty = "None": the next connect leaves
// AppSettings.FixedRegion empty so Photon pings every region and picks the best one - and the
// region it lands on is then SAVED as the selection (RememberConnectedRegion), so every later
// connect goes straight to it without re-pinging. Picking "None" again in MenuSettingsPopup is how
// a player asks for a fresh best-region search (e.g. after moving, or on a bad connection).
//
// Photon rooms are per region: two party members on different regions can't see each other's
// room code, so a manual pick that differs from a friend's is the first thing to check when
// "room not found" comes up.
public static class PhotonRegionSettings
{
    public readonly struct RegionOption
    {
        public readonly string Code;
        public readonly string DisplayName;

        public RegionOption(string code, string displayName)
        {
            Code = code;
            DisplayName = displayName;
        }
    }

    // Only the regions enabled for this app in the Photon dashboard (region whitelist "eu;us;sa") -
    // offering one outside the whitelist would just fail to connect. Keep in sync with the dashboard.
    // Static rather than read from RegionHandler so the dropdown is populated before any connection.
    public static readonly RegionOption[] Regions =
    {
        new("eu", "Europe"),
        new("us", "USA East"),
        new("sa", "South America"),
    };

    // Suffixed like photon_user_id so Multiplayer Play Mode virtual players don't share it.
    private static readonly PlayerPrefString RegionPref =
        new PlayerPrefString("photon_region" + LocalClientIdentity.PrefSuffix, "");

    // Fired when the selection changes, including the auto-save after a best-region connect, so an
    // open settings popup can refresh its dropdown.
    public static event Action Changed;

    // Empty = None (auto / best region).
    public static string SelectedRegion
    {
        get => RegionPref.Value ?? string.Empty;
        set
        {
            value = Normalize(value);
            if (string.Equals(RegionPref.Value, value, StringComparison.Ordinal))
                return;

            RegionPref.Value = value;
            LogHelper.Log("MatchMaking", string.IsNullOrEmpty(value)
                ? "Photon region set to None - next connect will search for the best region."
                : $"Photon region set to '{value}'.");
            Changed?.Invoke();
        }
    }

    public static bool IsAuto => string.IsNullOrEmpty(SelectedRegion);

    // Called right before every normal connect. A reconnect overwrites FixedRegion with the region
    // the room lives in afterwards (MatchmakingExtensions.ConnectToRoomAsync), which is correct.
    public static void ApplyTo(AppSettings settings)
    {
        if (settings == null)
            return;

        settings.FixedRegion = IsAuto ? null : SelectedRegion;

        // "None" means the player explicitly asked for a new search - don't let a cached ping
        // summary short-circuit it.
        if (IsAuto)
            settings.BestRegionSummaryFromStorage = null;
    }

    // Called once connected to master: under "None", the region Photon just picked becomes the
    // saved selection. A manual pick is never overwritten.
    public static void RememberConnectedRegion(string connectedRegion)
    {
        if (!IsAuto)
            return;

        connectedRegion = Normalize(connectedRegion);
        if (string.IsNullOrEmpty(connectedRegion))
            return;

        LogHelper.Log("MatchMaking", $"Best Photon region found: '{connectedRegion}' - saved as the selected region.");
        SelectedRegion = connectedRegion;
    }

    public static string GetDisplayName(string code)
    {
        foreach (var region in Regions)
        {
            if (string.Equals(region.Code, code, StringComparison.OrdinalIgnoreCase))
                return region.DisplayName;
        }

        return code;
    }

    // Region codes can come back with a cluster suffix ("eu/*"); only the bare code is stored.
    private static string Normalize(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return string.Empty;

        int slash = code.IndexOf('/');
        return (slash >= 0 ? code.Substring(0, slash) : code).Trim().ToLowerInvariant();
    }
}
