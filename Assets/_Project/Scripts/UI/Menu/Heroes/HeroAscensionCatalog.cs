using System.Collections.Generic;
using Quantum;

public enum AscensionKind
{
    Skill,
    Passive,
    Dash
}

/// <summary>How the Ascensions codex shows one Ascension line.</summary>
public enum AscensionState
{
    /// <summary>At least its first level found in a run: real name and icon; each level is discovered on its own.</summary>
    Discovered,

    /// <summary>Unlocked but no level found yet: name and effect hidden (???).</summary>
    Undiscovered,

    /// <summary>Needs something first (e.g. a Hero Level): only the requirement shows.</summary>
    Locked
}

/// <summary>One Ascension line of a hero: the upgrade asset (icon, name, descriptions) and which slot it builds on.</summary>
public readonly struct AscensionLine
{
    public readonly AscensionKind Kind;
    public readonly UpgradeData Upgrade;

    public AscensionLine(AscensionKind kind, UpgradeData upgrade)
    {
        Kind = kind;
        Upgrade = upgrade;
    }

    public string Name => string.IsNullOrEmpty(Upgrade.DisplayName) ? Upgrade.name : Upgrade.DisplayName;

    public int MaxRank => Upgrade is IRankedUpgrade ranked ? ranked.MaxRank : 1;

    public string RankDescription(int rank) => Upgrade is IRankedUpgrade ranked ? ranked.GetDescription(rank) : Upgrade.GetDescription();
}

/// <summary>
/// Reads a hero's Ascensions straight from its CharacterData - the same lists the level-up offers from, so the codex
/// always matches the game: Skill = the Hero Skill's actions authored inactive (see LevelUpUtility
/// .AddHeroSkillUpgradeCandidates), Passive = PassiveUpgrades, Dash = DashSkillUpgrades.
/// </summary>
public static class HeroAscensionCatalog
{
    public static List<AscensionLine> Lines(CharacterData hero)
    {
        var lines = new List<AscensionLine>();
        if (hero == null)
            return lines;

        if (QuantumUnityDB.TryGetGlobalAsset(hero.HeroSkill, out SkillData heroSkill) && heroSkill.Actions != null)
        {
            foreach (AssetRef<SkillActionData> actionRef in heroSkill.Actions)
            {
                if (QuantumUnityDB.TryGetGlobalAsset(actionRef, out SkillActionData action) && action.Activated == false)
                    lines.Add(new AscensionLine(AscensionKind.Skill, action));
            }
        }

        foreach (AssetRef<PassiveUpgradeData> passiveRef in hero.PassiveUpgrades)
        {
            if (QuantumUnityDB.TryGetGlobalAsset(passiveRef, out PassiveUpgradeData passive))
                lines.Add(new AscensionLine(AscensionKind.Passive, passive));
        }

        foreach (AssetRef<SkillActionData> dashRef in hero.DashSkillUpgrades)
        {
            if (QuantumUnityDB.TryGetGlobalAsset(dashRef, out SkillActionData dash))
                lines.Add(new AscensionLine(AscensionKind.Dash, dash));
        }

        return lines;
    }
}

/// <summary>
/// Per-player Ascension progress for the codex. Each level of a line is discovered on its own (finding level 2 in a run
/// doesn't reveal level 3). No save data exists yet, so this returns MOCK data showing every case: per kind, the 1st line
/// has 2 of 3 levels discovered, the 2nd is Locked (Hero Lv 8), the 3rd has 1 level, the 4th is Locked (Hero Lv 12), the
/// rest none. Replace both methods with the real unlock / discovery lookups.
/// </summary>
public static class HeroAscensionProgress
{
    // TODO: unlock (Hero Level) from the save once it exists.
    public static bool IsLocked(string heroId, AscensionLine line, int indexInKind, out string requirement)
    {
        requirement = indexInKind == 1 ? "Reach Hero Lv 8" : indexInKind == 3 ? "Reach Hero Lv 12" : null;
        return requirement != null;
    }

    // TODO: how many of the line's levels the player has met in a run, from the save once it exists.
    public static int GetDiscoveredLevels(string heroId, AscensionLine line, int indexInKind) => indexInKind switch
    {
        0 => 2,
        2 => 1,
        _ => 0
    };

    public static AscensionState GetState(string heroId, AscensionLine line, int indexInKind, out string requirement, out int discoveredLevels)
    {
        discoveredLevels = 0;
        if (IsLocked(heroId, line, indexInKind, out requirement))
            return AscensionState.Locked;

        discoveredLevels = GetDiscoveredLevels(heroId, line, indexInKind);
        return discoveredLevels > 0 ? AscensionState.Discovered : AscensionState.Undiscovered;
    }
}
