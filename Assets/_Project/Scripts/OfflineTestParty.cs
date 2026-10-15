using System.Collections.Generic;
using UnityEngine;

// Local-testing setup for the offline mode: how many bots join the human and which hero each plays
// (see docs/bots.md). Empty by default, so nothing changes unless a tester picks bots - the cheat-only
// OfflineTestPartyPanel (menu) and CheatMenu's Debug tab are what edit it. MatchMakingConfig.AddLocalPlayers
// reads it for offline, non-Training-Zone sessions; RestartOfflineMatch reuses it, so a restart keeps the party.
public static class OfflineTestParty
{
    // Human + bots can never exceed the Quantum player cap the sim is sized for.
    public const int MaxBots = 3;

    private const string PrefsKey = "OfflineTestParty";

    // One entry per bot: a CharacterCatalog.Entry id.
    private static List<string> _botHeroIds;

    public static int BotCount => Load().Count;

    public static IReadOnlyList<string> BotHeroIds => Load();

    // 0 -> 1 -> ... -> MaxBots -> 0. A new bot takes the first hero its predecessor isn't playing.
    public static void CycleBotCount()
    {
        List<string> ids = Load();

        if (ids.Count >= MaxBots)
        {
            ids.Clear();
        }
        else
        {
            CharacterCatalog catalog = Catalog();
            string id = catalog != null && catalog.characters.Length > 0
                ? catalog.characters[ids.Count % catalog.characters.Length].id
                : string.Empty;
            ids.Add(id);
        }

        Save();
    }

    public static void CycleHero(int botIndex)
    {
        List<string> ids = Load();
        CharacterCatalog catalog = Catalog();

        if (botIndex < 0 || botIndex >= ids.Count || catalog == null || catalog.characters.Length == 0)
            return;

        int current = IndexOf(catalog, ids[botIndex]);
        ids[botIndex] = catalog.characters[(current + 1) % catalog.characters.Length].id;
        Save();
    }

    public static string HeroLabel(int botIndex)
    {
        List<string> ids = Load();
        CharacterCatalog catalog = Catalog();

        if (botIndex < 0 || botIndex >= ids.Count || catalog == null)
            return "?";

        int index = IndexOf(catalog, ids[botIndex]);
        string name = catalog.characters[index].displayName;
        return string.IsNullOrEmpty(name) ? catalog.characters[index].id : name;
    }

    private static CharacterCatalog Catalog()
    {
        return PartyManager.Instance != null ? PartyManager.Instance.characterCatalog : null;
    }

    private static int IndexOf(CharacterCatalog catalog, string id)
    {
        for (int i = 0; i < catalog.characters.Length; i++)
            if (catalog.characters[i].id == id)
                return i;

        return 0;
    }

    private static List<string> Load()
    {
        if (_botHeroIds != null)
            return _botHeroIds;

        _botHeroIds = new List<string>();

        try
        {
            string raw = PlayerPrefs.GetString(PrefsKey, string.Empty);
            if (string.IsNullOrEmpty(raw) == false)
                _botHeroIds.AddRange(raw.Split(','));
        }
        catch (System.Exception) { }

        if (_botHeroIds.Count > MaxBots)
            _botHeroIds.RemoveRange(MaxBots, _botHeroIds.Count - MaxBots);

        return _botHeroIds;
    }

    private static void Save()
    {
        try
        {
            PlayerPrefs.SetString(PrefsKey, string.Join(",", _botHeroIds));
            PlayerPrefs.Save();
        }
        catch (System.Exception) { }
    }
}
