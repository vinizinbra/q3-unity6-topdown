namespace Quantum
{
    using UnityEngine;

    // Base for a Cursed Rift sacrifice option (see docs/breathing-poi.md) - deliberately NOT a
    // subtype of UpgradeData, since a Sacrifice isn't an Upgrade and has no Rarity to show (spec
    // is explicit about this). Same "abstract base + one concrete subclass per variation" shape
    // as RiftMutationData/GlobalUpgradeData/PassiveUpgradeData though, for the same reason:
    // eligibility/cost/preview all differ per kind and belong on the asset itself, not a switch
    // inside CursedRiftUtility.
    public abstract class SacrificeDefinition : AssetObject
    {
        public Sprite Icon;
        public string DisplayName;

        // Short evocative word shown above DisplayName on the card (e.g. "BLOOD"/"WEALTH"/"RIFT")
        // - distinct from DisplayName ("Blood Offering") and from the constant "RIFT SACRIFICE"
        // category label every sacrifice card shows (see GameplayUiController.BuildSacrificeCardData).
        public string TopLabel;

        [TextArea]
        public string Description;

        // Card button text (e.g. "SACRIFICE"/"PAY") - empty falls back to "SACRIFICE".
        public string ButtonLabel;

        // Draw weight among currently-ELIGIBLE sacrifices only (see
        // CursedRiftUtility.RollSacrificeOptions) - no rarity axis here, just a flat relative
        // weight, unlike LevelUpConfig's rarity-tiered weighting.
        public int Weight = 100;

        public abstract bool IsEligible(Frame f, EntityRef entity);
        public abstract void ApplyCost(Frame f, EntityRef entity);

        // Live "before -> after" text (e.g. "MAX HP 100 -> 80") - computed fresh off current Frame
        // state every time the View asks, never cached, so it can never go stale between roll and
        // confirm (see UpgradeCardWidget.CardData.ValuePreview).
        public abstract string BuildValuePreview(Frame f, EntityRef entity);
    }
}
