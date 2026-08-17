namespace Quantum
{
    using System.Collections.Generic;

    // Store's food/utility offer pool - same list-of-AssetRef shape SacrificePoolData already uses.
    // No rarity-weight table (unlike WeaponPerkPoolData) - each FoodOfferData carries its own flat
    // Weight instead, same convention SacrificeDefinition.Weight already uses.
    public class FoodOfferPoolData : AssetObject
    {
        [ExpandableAsset] public List<AssetRef<FoodOfferData>> Foods = new();
    }
}
