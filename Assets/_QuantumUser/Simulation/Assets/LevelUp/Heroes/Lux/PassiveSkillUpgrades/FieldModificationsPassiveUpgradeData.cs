namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Ranked Passive Ascension (Field Modifications, line 3/3) - the line that turns Lux's economy
    // into an ACTIVE loop rather than a passive trickle: deploy the machine, then feed it Scrap before
    // it expires to make it better while it's still alive.
    //
    //  - Rank 1: each Scrap collected while she owns an active Sentry gives THAT Sentry more damage,
    //    up to a stack cap.
    //  - Rank 2: each stack also gives it Fire Rate.
    //  - Rank 3 "MK II": at max stacks the Sentry's primary Cannon is upgraded to a Twin Cannon.
    //
    // Stacks live on the SENTRY (SentryModifications), not on Lux - which is what makes them last "for
    // that Sentry's lifetime" and reset on redeploy, exactly as specified. When several Sentries are
    // active, Scrap goes to the most recently deployed one (SentryUtility.FindNewestOwnedSentry) -
    // the brief's own preferred starting rule, deliberately not "buff every Sentry at once".
    //
    // MK II is a WEAPON SWAP, not a second turret implementation: the slot-0 barrel is re-equipped
    // with a different WeaponDataAsset through the ordinary WeaponSystem.Equip path, so a Twin Cannon
    // is just authored weapon data (2 projectiles, ~70% damage each) like any other gun.
    public unsafe partial class FieldModificationsPassiveUpgradeData : PassiveUpgradeData
    {
        [Tooltip("Sentry damage added per stack.")]
        public FP[] DamagePerStack = { FP.FromString("0.04"), FP.FromString("0.04"), FP.FromString("0.04") };

        [Tooltip("Rank 2+ - Sentry Fire Rate added per stack, on top of the damage.")]
        public FP[] FireRatePerStack = { FP._0, FP.FromString("0.03"), FP.FromString("0.03") };

        public byte[] MaxStacks = { 5, 5, 5 };

        [Header("Rank 3 - MK II")]
        [Tooltip("Replaces the Sentry's slot-0 Cannon at max stacks. A Twin Cannon is ordinary weapon data - 2 projectiles at ~70% damage each - not a separate turret implementation.")]
        [ExpandableAsset] public AssetRef<WeaponDataAsset> MkIIWeapon;

        public override void Apply(Frame f, EntityRef entity, int rank)
        {
            if (f.Unsafe.TryGetPointer<LuxScrapCollector>(entity, out var collector) == false)
                return;

            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            collector->FieldModDamagePerStack = DamagePerStack[index];
            collector->FieldModFireRatePerStack = FireRatePerStack[index];
            collector->FieldModMaxStacks = MaxStacks[index];
            collector->MkIIWeapon = rank >= 3 ? MkIIWeapon : default;
        }
    }
}
