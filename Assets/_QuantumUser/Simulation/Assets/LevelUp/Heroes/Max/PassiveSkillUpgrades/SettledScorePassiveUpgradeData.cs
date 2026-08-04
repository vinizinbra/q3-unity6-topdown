namespace Quantum
{
    using Photon.Deterministic;

    // Vendetta Upgrade - increases Vendetta's on-kill heal fraction. Composes onto the shared
    // RevengeConfig via FPMath.Max so re-picking (or stacking with a lower-tier duplicate) can
    // never downgrade an already-granted bonus.
    public unsafe partial class SettledScorePassiveUpgradeData : PassiveUpgradeData
    {
        public FP HealMultiplier = FP._1;

        public override void Apply(Frame f, EntityRef entity)
        {
            f.AddOrGet<RevengeConfig>(entity, out var config);
            config->HealMultiplier = FPMath.Max(config->HealMultiplier, HealMultiplier);
        }
    }
}
