namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Lux's Weapon Family Mastery - Assault Rifle (see docs/hero-mastery.md).
    //  R1/R2/R3: +15% / +30% / +50% Assault Rifle Damage.
    //  R3 "Targeting Link": enemies hit by Lux's own AR take additional Damage from HER Sentries
    //  specifically - a source-filtered target modifier (HeroMasteryUtility.ApplyTargetingLinkMark/
    //  GetTargetingLinkMultiplier), never a global incoming-damage buff and never another player's
    //  Sentry. Repeated AR hits refresh the mark's duration rather than stacking.
    public unsafe partial class AssaultRifleMasteryData : WeaponFamilyMasteryData
    {
        [Tooltip("Targeting Link (R3) - additional Damage Lux's own Sentries deal to a marked target.")]
        public FP TargetingLinkSentryDamageBonus = FP.FromString("0.25");

        [Tooltip("Targeting Link (R3) - how long the mark lasts before a fresh AR hit is needed to refresh it.")]
        public FP TargetingLinkMarkDuration = 4;

        protected override void ApplyRank(Frame f, EntityRef entity, int rank)
        {
            if (rank < 3)
                return;

            f.AddOrGet<TargetingLinkUpgrade>(entity, out var targetingLink);
            targetingLink->SentryDamageBonus = TargetingLinkSentryDamageBonus;
            targetingLink->MarkDuration = TargetingLinkMarkDuration;
        }
    }
}
