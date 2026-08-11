namespace Quantum
{
    using Photon.Deterministic;

    // Ranked Ascension - deals bonus damage against any enemy currently carrying ExplodeOnDeath
    // ("Unstable" - marked to explode on death, from Chain Reaction or any other source), read live
    // in DamageUtility.ResolveOutgoingDamage for every damage source Pixie has (weapon, Bunny Bomb,
    // explosions, dash explosions, etc. - that resolution point runs for all of them, not a bomb-only
    // hook). Distinct from Unstable Mixture's BonusDamageMultiplier, which only scales a marked
    // enemy's own death-explosion payout - see MarkExplosiveDeath.qtn's own comment on the two. Each
    // rank SETS the total multiplier (not additive across ranks).
    public unsafe partial class UnstableTargetingPassiveUpgradeData : PassiveUpgradeData
    {
        public FP[] DamageMultiplier = { FP.FromString("1.20"), FP.FromString("1.35"), FP.FromString("1.50") };

        public override void Apply(Frame f, EntityRef entity, int rank)
        {
            if (f.Unsafe.TryGetPointer<MarkExplosiveDeath>(entity, out var mark) == false)
                return;

            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;
            mark->DamageBonusVsUnstable = DamageMultiplier[index];
        }
    }
}
