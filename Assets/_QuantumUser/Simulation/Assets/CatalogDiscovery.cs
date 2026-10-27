namespace Quantum
{
    // View-only: when the menu Catalog reveals an entry (an enemy, a boss or a Rift Mutation). Lives on the
    // gameplay asset itself (EnemyDataAsset.View.cs / RiftMutationData.View.cs) so the Catalog never needs a
    // second asset per entry - see CatalogTab and CatalogProgress.
    public enum CatalogDiscovery : byte
    {
        // Listed as "???" until the player's progress says it was discovered (enemy seen, mutation offered).
        OnEncounter,
        // Always revealed.
        AlwaysKnown,
        // Not listed at all until discovered - secret entries.
        HiddenUntilFound
    }
}
