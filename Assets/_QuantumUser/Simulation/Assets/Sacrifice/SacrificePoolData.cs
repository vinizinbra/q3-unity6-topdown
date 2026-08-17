namespace Quantum
{
    using System.Collections.Generic;

    // Cursed Rift's sacrifice pool - same list-of-AssetRef shape WeaponPerkPoolData already uses.
    // No rarity-weight table here (unlike WeaponPerkPoolData) - each SacrificeDefinition carries
    // its own flat Weight instead, since there's no rarity axis to sacrifices.
    public class SacrificePoolData : AssetObject
    {
        [ExpandableAsset] public List<AssetRef<SacrificeDefinition>> Sacrifices = new();
    }
}
