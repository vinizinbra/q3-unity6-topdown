namespace Quantum
{
    // Cursed Rift's own tuning - see CursedRiftUtility and docs/breathing-poi.md. The Rift
    // Mutation reward pool deliberately has no separate list here - it reuses
    // RuntimeConfig.LevelUpConfig.RiftMutations directly via LevelUpUtility.RollMutationOptions,
    // the exact same pool a normal level-up's RiftMutation category already draws from.
    public class CursedRiftConfig : AssetObject
    {
        public AssetRef<SacrificePoolData> SacrificePool;
        public int SacrificeChoiceCount = 3;
        public int MutationChoiceCount = 3;
    }
}
