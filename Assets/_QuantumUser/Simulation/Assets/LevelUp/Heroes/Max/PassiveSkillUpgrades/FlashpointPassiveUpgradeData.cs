namespace Quantum
{
    using Photon.Deterministic;

    // Fire Mastery trait - a crit against an already-Burning target detonates a capped-radius
    // explosion centered on it (see MaxFireMasteryReactionSystem.OnCriticalHit). ProcCooldown
    // composes as a minimum (the lowest authored cooldown wins, letting a stronger rank fire more
    // often) rather than FPMath.Max, unlike every other field here.
    public unsafe partial class FlashpointPassiveUpgradeData : PassiveUpgradeData
    {
        public FP Radius = 3;
        public FP DamageCoefficient = FP._0_50;
        public FP ProcCooldown = 2;
        public int MaxTargets = 5;
        public bool AllowRecursiveProc;

        public override void Apply(Frame f, EntityRef entity)
        {
            f.AddOrGet<ExplosionOnConditionalHit>(entity, out var explosion);
            explosion->Radius = FPMath.Max(explosion->Radius, Radius);
            explosion->DamageCoefficient = FPMath.Max(explosion->DamageCoefficient, DamageCoefficient);
            explosion->ProcCooldown = explosion->ProcCooldown > FP._0 ? FPMath.Min(explosion->ProcCooldown, ProcCooldown) : ProcCooldown;
            explosion->MaxTargets = explosion->MaxTargets > MaxTargets ? explosion->MaxTargets : MaxTargets;
            explosion->AllowRecursiveProc |= AllowRecursiveProc;
        }
    }
}
