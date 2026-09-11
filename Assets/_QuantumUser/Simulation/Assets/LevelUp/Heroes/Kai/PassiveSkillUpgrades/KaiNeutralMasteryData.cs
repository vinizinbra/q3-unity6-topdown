namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Kai's Element Mastery - Neutral (see docs/hero-mastery.md). Same weaker curve every Neutral
    // Mastery line authors (10/20/40, not the standard 15/30/50) - not a special case in code, just
    // this instance's own authored DamageMultiplierPerRank.
    //  R1/R2/R3: +10% / +20% / +40% Neutral Weapon Damage.
    //  R3 "Ghost Shot": every REAL pellet/projectile the FIRST shot of a fresh magazine launches (hit
    //  or miss) is echoed a moment later, dealing 50% of that pellet's own pre-hit damage. Configuration
    //  only - the actual behavior is the generic, hero-agnostic Damage Echo system
    //  (DamageEchoUpgrade/PendingDamageEcho/DamageEchoSystem, see DamageEcho.qtn) - this class just
    //  installs it with Kai's own tuned values.
    public unsafe partial class KaiNeutralMasteryData : ElementMasteryData
    {
        [Tooltip("Ghost Shot (R3) - fraction of the pellet's own pre-hit damage dealt again by its echo.")]
        public FP GhostShotDamageMultiplier = FP._0_50;

        [Tooltip("Ghost Shot (R3) - delay in seconds before the echo lands.")]
        public FP GhostShotDelay = FP.FromString("0.2");

        [Tooltip("Ghost Shot (R3) - presentation config for the echo's own impact particle (see DamageEchoVisualData.View.cs for the actual prefab field). Optional - falls back to EffectsManager's default area blast effect if left unassigned.")]
        public AssetRef<DamageEchoVisualData> GhostShotVisual;

        [Tooltip("Ghost Shot (R3) - the ProjectileHitData substituted onto the echo projectile (EchoHitData is the one used today). Required for Ghost Shot to fly as a real projectile - spawned using the owner's CURRENTLY EQUIPPED weapon's own ProjectileDataAsset (same Prototype/Movement) - rather than dealing instant damage. See DamageEchoUpgrade.EchoHit's own comment.")]
        public AssetRef<ProjectileHitData> GhostShotHit;

        protected override void ApplyRank(Frame f, EntityRef entity, int rank)
        {
            if (rank < 3)
                return;

            f.AddOrGet<DamageEchoUpgrade>(entity, out var echo);
            echo->RequiredElement = ElementType.Neutral;
            echo->DamageMultiplier = GhostShotDamageMultiplier;
            echo->Delay = GhostShotDelay;
            echo->Visual = GhostShotVisual;
            echo->EchoHit = GhostShotHit;
        }
    }
}
