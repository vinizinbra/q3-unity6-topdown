namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Base for a Store food/utility offer (see docs/store-blacksmith.md) - mirrors
    // SacrificeDefinition's own shape one-for-one (abstract base + one concrete subclass per
    // variation, rendered via the existing UpgradeCardWidget with no switch statement anywhere),
    // since a food offer is the same "small, self-describing, instantly-resolved effect" idea as a
    // Sacrifice, just a reward bought with Coins instead of a cost paid for a Rift Mutation.
    // Deliberately NOT a subtype of UpgradeData - a food item isn't a level-up upgrade, it's
    // consumed immediately on purchase with nothing persisted (see docs/store-blacksmith.md's MVP
    // scope - no food inventory).
    public abstract class FoodOfferData : AssetObject
    {
        public Sprite Icon;
        public string DisplayName;

        // Short evocative word shown above DisplayName on the card (e.g. "HEAL"/"SHIELD"/"SPEED"/
        // "DAMAGE") - same role as SacrificeDefinition.TopLabel.
        public string TopLabel;

        [TextArea]
        public string Description;

        // Card button text (e.g. "BUY") - empty falls back to "BUY".
        public string ButtonLabel;

        public FP Price;

        // Draw weight among the food pool (see StoreUtility.RollFoodOffers) - flat relative weight,
        // same convention SacrificeDefinition.Weight already uses, no rarity axis.
        public int Weight = 100;

        public abstract void Apply(Frame f, EntityRef buyer);
    }
}
