namespace Quantum
{
    using Photon.Deterministic;

    // Ranked Vortex Ascension (Void Shards, line 4/4) - see docs/kai-ascensions.md. Repurposes the old
    // (single-pick) VortexHomingProjectileSkillAction - the vortex periodically fires homing Void
    // Shards at nearby enemies (see VortexHomingProjectileUpgrade/VortexSystem.TryHomingProjectile).
    // Rank 2 fires faster, hits harder, reaches farther, and pierces 2 enemies per shard; rank 3 fires
    // 2 shards per volley (preferring distinct targets when available), each piercing 3 enemies.
    //
    // Begin-only, deliberately not paired with End - same reasoning as every other "configures what
    // gets spawned" upgrade this session: re-granting fresh (idempotent) every activation and never
    // removing it means it's simply always there once picked, with nothing to race against.
    public unsafe partial class VoidShardsSkillAction : SkillActionData
    {
        [ExpandableAsset] public AssetRef<ProjectileDataAsset> Projectile;

        public FP[] DamagePercent = { FP.FromString("0.30"), FP.FromString("0.40"), FP.FromString("0.45") };
        public FP[] TickInterval = { FP._1, FP.FromString("0.75"), FP.FromString("0.75") };
        public FP[] SearchRadiusMultiplier = { FP._3, FP._5, FP._5 };
        public byte[] ShardCount = { 1, 1, 2 };

        // 1 (rank 1) reproduces the default "stops on the first enemy" homing-shot behavior exactly.
        public byte[] PierceCount = { 1, 2, 3 };

        public VoidShardsSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase firedPhase, AssetRef<SkillActionData> selfRef)
        {
            int rank = System.Math.Max(1, SkillUpgradeUtility.GetRank(f, filter.Entity, selfRef));
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            f.AddOrGet<VortexHomingProjectileUpgrade>(filter.Entity, out var upgrade);
            upgrade->Projectile = Projectile;
            upgrade->Damage = DamagePercent[index] * KaiAscensionUtility.ResolveVortexSkillDamage(f, filter.Entity);
            upgrade->SearchRadiusMultiplier = SearchRadiusMultiplier[index];
            upgrade->TickInterval = TickInterval[index];
            upgrade->ShardCount = ShardCount[index];
            upgrade->PierceCount = PierceCount[index];
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }
    }
}
