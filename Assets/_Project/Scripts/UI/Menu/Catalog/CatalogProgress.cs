using Quantum;

/// <summary>What the player has done with one Catalog entry (seen, killed, picked...).</summary>
public struct CatalogRecord
{
    public bool Discovered;
    public int Encountered;
    public int Defeated;

    /// <summary>Bosses only: fastest clear, in seconds. 0 = none yet.</summary>
    public int BestClearSeconds;
}

/// <summary>
/// The Catalog's view of the player's progress, keyed by the gameplay asset's own Quantum <see cref="AssetGuid"/>
/// (already unique and stable, so the save needs no extra id). Nothing records it yet: every lookup returns an
/// empty record until the run starts writing encounters / kills / picks into the save.
/// </summary>
public static class CatalogProgress
{
    public static CatalogRecord Get(AssetGuid guid)
    {
        // TODO: read from MetaProgressionSave once the run records Catalog progress.
        return default;
    }

    public static bool IsDiscovered(CatalogDiscovery rule, in CatalogRecord record)
    {
        return rule == CatalogDiscovery.AlwaysKnown || record.Discovered;
    }

    public static CatalogDiscovery RuleOf(AssetObject asset)
    {
        if (asset is EnemyDataAsset enemy)
            return enemy.Discovery;
        return asset is RiftMutationData mutation ? mutation.Discovery : CatalogDiscovery.OnEncounter;
    }
}
