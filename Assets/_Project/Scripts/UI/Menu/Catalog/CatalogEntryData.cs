using UnityEngine;

public enum CatalogKind
{
    Enemy,
    Boss,
    RiftMutation
}

/// <summary>
/// One entry of the Catalog screen (an enemy, a boss or a Rift Mutation). It only holds what is
/// presentation (world, role, mock progress); name, description, icon and - for enemies - the section
/// (faction) are read from <see cref="source"/> (EnemyDataAsset, RiftMutationData...) via the Resolved*
/// members. <see cref="group"/> / <see cref="groupColor"/> only apply where the source has no faction
/// (bosses are listed by world, mutations by rarity). UI only for now: discovery and the
/// encountered/defeated counters are mocked until the player's progress/save class exists.
/// </summary>
[CreateAssetMenu(fileName = "CatalogEntry", menuName = "RiftRaiders/Catalog Entry")]
public class CatalogEntryData : ScriptableObject
{
    [Header("Identity")]
    public CatalogKind kind;

    [Tooltip("Section for entries whose source has no faction (a boss's world, a mutation's rarity). Enemies use their EnemyDataAsset.Faction instead.")]
    public string group;

    [Tooltip("Section header color, same scope as Group.")]
    public Color groupColor = new Color(0.992f, 0.224f, 0.443f);

    [Tooltip("Short descriptor shown after the group in the detail panel (RANGED, MELEE...).")]
    public string subtitle;

    [Tooltip("Optional override. Left empty, the icon comes from the source (an enemy's view sprite, a mutation's icon).")]
    public Sprite icon;

    [Tooltip("Tints the icon (white = untouched). Darken it for a generic placeholder icon so it reads on the light tile.")]
    public Color iconTint = Color.white;

    [Tooltip("WorldCatalog index this entry belongs to; -1 = not world specific.")]
    public int worldIndex = -1;

    [Tooltip("The gameplay asset this describes (EnemyDataAsset, RiftMutationData...). The source of the name, description and icon.")]
    public Object source;

    [Header("Mock (until there is save data)")]
    public bool mockDiscovered = true;
    public int mockEncountered;
    public int mockDefeated;

    [Tooltip("Bosses only: fastest clear, in seconds. 0 = none yet.")]
    public int mockBestClearSeconds;

    // Enemies read EnemyName/Description off the EnemyDataAsset; an Elite-tier asset gets the same " Elite"
    // suffix the in-game name callout appends (EnemyView.ResolveEnemyName). Mutations read their own
    // DisplayName/Icon/GetDescription(), which is also filled from live balance values.
    public string ResolvedName
    {
        get
        {
            if (source is Quantum.EnemyDataAsset enemy)
            {
                string baseName = string.IsNullOrEmpty(enemy.EnemyName) ? enemy.name : enemy.EnemyName;
                return enemy.Tier == Quantum.EnemyTier.Elite ? baseName + " Elite" : baseName;
            }

            if (source is Quantum.UpgradeData upgrade && !string.IsNullOrEmpty(upgrade.DisplayName))
                return upgrade.DisplayName;

            return name;
        }
    }

    // Enemies are grouped by their faction; everything else by the entry's own group.
    public string ResolvedGroup => source is Quantum.EnemyDataAsset enemy && enemy.Faction != null && !string.IsNullOrEmpty(enemy.Faction.DisplayName) ? enemy.Faction.DisplayName : group;

    public Color ResolvedGroupColor => source is Quantum.EnemyDataAsset enemy && enemy.Faction != null ? enemy.Faction.AccentColor : groupColor;

    public string ResolvedDescription
    {
        get
        {
            if (source is Quantum.EnemyDataAsset enemy && !string.IsNullOrEmpty(enemy.Description))
                return enemy.Description;

            if (source is Quantum.UpgradeData upgrade)
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
            if (icon != null)
                return icon;

            // Bosses show their full key art (the same sprite as the boss reveal card).
            if (source is Quantum.BossDataAsset boss && boss.UiSprite != null)
                return boss.UiSprite;

            if (source is Quantum.EnemyDataAsset enemy && enemy.ViewPrefab != null)
            {
                var renderer = enemy.ViewPrefab.GetComponentInChildren<SpriteRenderer>(true);
                return renderer != null ? renderer.sprite : null;
            }

            return source is Quantum.UpgradeData upgrade ? upgrade.Icon : null;
        }
    }
}
