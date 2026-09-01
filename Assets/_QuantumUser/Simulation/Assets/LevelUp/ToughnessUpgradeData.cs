namespace Quantum
{
    using Photon.Deterministic;

    // Replaces the old flat "+10 Shield" ShieldUpgradeData (removed) as the pool's defensive pick -
    // Shield stopped being a free auto-regenerating pool and became an earned, charge-only buffer
    // whose job is keeping the Accessory on your head (see docs/accessory-guard.md), so a repeatable
    // "+N Max Shield" no longer reads as flat survivability.
    //
    // Multiplies CharacterStats.DamageTakenMultiplier rather than adding to the DamageReduction
    // fraction, which is what makes it safe to stack indefinitely like every other upgrade in this
    // pool - 0.9 compounds toward 0 without ever reaching immunity. See docs/global-upgrades.md.
    public unsafe class ToughnessUpgradeData : CharacterStatMultiplierUpgradeData
    {
        protected override FP* GetStat(CharacterStats* stats) => &stats->DamageTakenMultiplier;
    }
}
