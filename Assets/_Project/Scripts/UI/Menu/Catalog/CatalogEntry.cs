using Quantum;
using UnityEngine;

public enum CatalogKind
{
    Enemy,
    Boss,
    RiftMutation
}

/// <summary>
/// One entry of the Catalog screen (an enemy, a boss or a Rift Mutation), built at runtime by
/// <see cref="CatalogTab"/> from the game's own data - never authored. Name, description, icon, section and
/// subtitle are all read from <see cref="source"/> (EnemyDataAsset, RiftMutationData...) via the Resolved*
/// members; <see cref="record"/> / <see cref="discovered"/> come from <see cref="CatalogProgress"/>.
/// </summary>
public class CatalogEntry
{
    public CatalogKind kind;

    /// <summary>Enemies and bosses: the first world that spawns them. -1 for mutations.</summary>
    public int worldIndex = -1;

    public AssetObject source;
    public CatalogRecord record;
    public bool discovered;

    /// <summary>Mutations only: the section header color for their rarity (see CatalogTab.rarityColors).</summary>
    public Color rarityColor = Color.white;

    public string Name => source != null ? source.name : string.Empty;

    // Enemies read EnemyName off the EnemyDataAsset; an Elite-tier asset gets the same " Elite" suffix the
    // in-game name callout appends (EnemyView.ResolveEnemyName). Mutations read their own DisplayName.
    public string ResolvedName
    {
        get
        {
            if (source is EnemyDataAsset enemy)
            {
                string baseName = string.IsNullOrEmpty(enemy.EnemyName) ? enemy.name : enemy.EnemyName;
                return enemy.Tier == EnemyTier.Elite ? baseName + " Elite" : baseName;
            }

            if (source is UpgradeData upgrade && !string.IsNullOrEmpty(upgrade.DisplayName))
                return upgrade.DisplayName;

            return Name;
        }
    }

    // Enemies (and bosses, when they have one) are grouped by their faction, mutations by rarity.
    public string ResolvedGroup
    {
        get
        {
            if (source is RiftMutationData mutation)
                return mutation.Rarity.ToString();
            if (source is EnemyDataAsset enemy && enemy.Faction != null && !string.IsNullOrEmpty(enemy.Faction.DisplayName))
                return enemy.Faction.DisplayName;
            return string.Empty;
        }
    }

    public Color ResolvedGroupColor
    {
        get
        {
            if (source is RiftMutationData)
                return rarityColor;
            if (source is EnemyDataAsset enemy && enemy.Faction != null)
                return enemy.Faction.AccentColor;
            return new Color(0.992f, 0.224f, 0.443f);
        }
    }

    // Short descriptor shown after the group in the detail panel (RANGED, MELEE... / PLAYER, RUN).
    public string ResolvedSubtitle
    {
        get
        {
            if (source is RiftMutationData mutation)
                return mutation.Scope.ToString();
            if (source is EnemyDataAsset enemy && !string.IsNullOrEmpty(enemy.CatalogSubtitle))
                return enemy.CatalogSubtitle;
            return kind == CatalogKind.Boss ? "Boss" : string.Empty;
        }
    }

    // Mutations' GetDescription() is filled from live balance values.
    public string ResolvedDescription
    {
        get
        {
            if (source is EnemyDataAsset enemy && !string.IsNullOrEmpty(enemy.Description))
                return enemy.Description;

            if (source is UpgradeData upgrade)
            {
                string text = upgrade.GetDescription();
                if (!string.IsNullOrEmpty(text))
                    return text;
            }

            return string.Empty;
        }
    }

    public Sprite ResolvedIcon
    {
        get
        {
            // Bosses show their full key art (the same sprite as the boss reveal card).
            if (source is BossDataAsset boss && boss.UiSprite != null)
                return boss.UiSprite;

            if (source is EnemyDataAsset enemy && enemy.ViewPrefab != null)
            {
                var renderer = enemy.ViewPrefab.GetComponentInChildren<SpriteRenderer>(true);
                return renderer != null ? renderer.sprite : null;
            }

            return source is UpgradeData upgrade ? upgrade.Icon : null;
        }
    }
}
