namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Hero Mastery - Element track (see docs/hero-mastery.md and WeaponFamilyMasteryData's own
    // comment, which this mirrors exactly). Neutral Mastery is NOT a special case in code - it is
    // simply an ElementMasteryData instance with Element = Neutral and its own (weaker) authored
    // DamageMultiplierPerRank curve (10/20/40 instead of 15/30/50); the generic damage-multiplier
    // resolution in HeroMasteryUtility never checks for Neutral by name.
    public abstract unsafe partial class ElementMasteryData : PassiveUpgradeData
    {
        [Tooltip("Fixed per hero - which element this Mastery line affects. Only matches a weapon whose own WeaponDataAsset.Element equals this, never \"every weapon regardless of element\".")]
        public ElementType Element;

        [Tooltip("R1/R2/R3 total damage multiplier (0.15 = +15%). Neutral Mastery authors its own weaker curve here (0.10/0.20/0.40) - see docs/hero-mastery.md.")]
        public FP[] DamageMultiplierPerRank = { FP.FromString("0.15"), FP.FromString("0.30"), FP._0_50 };

        public sealed override void Apply(Frame f, EntityRef entity, int rank)
        {
            int index = System.Math.Clamp(rank, 1, MaxRank) - 1;

            f.AddOrGet<ElementMastery>(entity, out var mastery);
            mastery->Element = Element;
            mastery->DamageMultiplier = DamageMultiplierPerRank[index];

            ApplyRank(f, entity, rank);
        }

        // Concrete Mastery lines override this for their own R3 Special.
        protected virtual void ApplyRank(Frame f, EntityRef entity, int rank) { }
    }
}
