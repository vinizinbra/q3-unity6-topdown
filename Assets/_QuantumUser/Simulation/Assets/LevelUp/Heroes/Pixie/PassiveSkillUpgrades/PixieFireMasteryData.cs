namespace Quantum
{
    using UnityEngine;

    // Pixie's Element Mastery - Fire (see docs/hero-mastery.md).
    //  R1/R2/R3: +15% / +30% / +50% Fire Weapon Damage.
    //  R3 "Incendiary Rounds": every Fire-weapon hit also detonates an area explosion at the target,
    //  guaranteed (unlike her separate, proc-chance Explosive Rounds Ascension). Fully configured via a
    //  real AreaHitData asset - same type Grenade Launcher's own weapon uses - rather than bespoke
    //  Radius/DamageMultiplier fields, so BlastRadius/TargetMask/Effects/TriggersSpawnUpgrades are all
    //  tunable in the Inspector like any other explosion in the game.
    public unsafe partial class PixieFireMasteryData : ElementMasteryData
    {
        [Tooltip("Incendiary Rounds (R3) - the AreaHitData a Fire-weapon hit detonates, centered on the target. BlastRadius/TargetMask/Effects (damage %, knockback, etc.) all live on this asset.")]
        public AssetRef<AreaHitData> ExplosiveShotArea;

        protected override void ApplyRank(Frame f, EntityRef entity, int rank)
        {
            if (rank < 3)
                return;

            f.AddOrGet<FireWeaponExplosiveShotUpgrade>(entity, out var explosive);
            explosive->Explosion = ExplosiveShotArea;
        }
    }
}
