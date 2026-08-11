namespace Quantum
{
    using Photon.Deterministic;

    // Ranked Vortex Ascension (Compression, line 2/4) - see docs/kai-ascensions.md. Merges the old
    // VortexDamagePulseSkillAction + VortexCrowdDamageSkillAction (VortexCrowdDamageSkillAction.cs
    // deleted) into one line. Rank 1 grants VortexDamageUpgrade (a real AreaDamage pulse - see
    // SpawnVortexEffectData/VortexSystem); rank 2 additionally grants VortexCrowdDamageUpgrade
    // (damage scales with how many enemies are currently trapped); rank 3 additionally grants
    // VortexImplosionUpgrade (every third pulse also detonates at the vortex's own center, itself
    // scaled by the same crowd multiplier).
    //
    // Begin-only, deliberately not paired with End - same reasoning as every other "configures what
    // gets spawned" upgrade this session: re-granting fresh (idempotent) every activation and never
    // removing it means it's simply always there once picked, with nothing to race against.
    public unsafe partial class CompressionSkillAction : SkillActionData
    {
        // Flat across ranks - only the crowd-scaling/Implosion mechanisms are what each rank adds, per
        // design ("Vortex deals damage every 0.5s... starting value 20%" - no per-rank pulse growth
        // called out beyond that).
        public FP PulseDamagePercent = FP._0_20;
        public FP PulseTickInterval = FP._0_50;
        [ExpandableAsset] public AssetRef<HitEffectData> DamageEffect;

        // Rank 2+ only (0 at rank 1, which reproduces "no crowd scaling" for free - VortexSystem's own
        // ResolveCrowdMultiplier already no-ops without VortexCrowdDamageUpgrade granted at all).
        public FP[] CrowdPerEnemyBonus = { FP._0, FP.FromString("0.08"), FP.FromString("0.08") };
        public byte[] CrowdMaxCount = { 0, 8, 8 };

        // Rank 3 only (0 at ranks 1-2, which leaves VortexImplosionUpgrade ungranted entirely).
        public FP[] ImplosionDamagePercent = { FP._0, FP._0, FP.FromString("0.75") };
        public byte[] ImplosionEveryNthPulse = { 0, 0, 3 };
        public FP ImplosionRadiusFraction = FP._0_50;

        public CompressionSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase firedPhase, AssetRef<SkillActionData> selfRef)
        {
            int rank = System.Math.Max(1, SkillUpgradeUtility.GetRank(f, filter.Entity, selfRef));
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            f.AddOrGet<VortexDamageUpgrade>(filter.Entity, out var damageUpgrade);
            damageUpgrade->Damage = PulseDamagePercent * KaiAscensionUtility.ResolveVortexSkillDamage(f, filter.Entity);
            damageUpgrade->TickInterval = PulseTickInterval;
            damageUpgrade->DamageEffect = DamageEffect;

            if (rank >= 2)
            {
                f.AddOrGet<VortexCrowdDamageUpgrade>(filter.Entity, out var crowdUpgrade);
                crowdUpgrade->PerEnemyBonus = CrowdPerEnemyBonus[index];
                crowdUpgrade->MaxCount = CrowdMaxCount[index];
            }

            if (rank >= 3)
            {
                f.AddOrGet<VortexImplosionUpgrade>(filter.Entity, out var implosionUpgrade);
                implosionUpgrade->DamagePercent = ImplosionDamagePercent[index];
                implosionUpgrade->RadiusFraction = ImplosionRadiusFraction;
                implosionUpgrade->EveryNthPulse = ImplosionEveryNthPulse[index];
                implosionUpgrade->PulseCounter = 0;
                implosionUpgrade->Source = this;
            }
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }
    }
}
