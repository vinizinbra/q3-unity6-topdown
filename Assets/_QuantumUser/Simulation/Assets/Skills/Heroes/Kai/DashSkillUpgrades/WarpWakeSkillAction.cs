namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;

    // Ranked Dash Ascension (Warp Wake, line 3/3) - see docs/kai-ascensions.md. Dashing drops a
    // temporary Void behind Kai that pulls nearby enemies inward - reuses Vortex/VortexSystem/
    // AreaDamage directly rather than a new system, since VortexSystem.Filter only requires
    // {Transform3D, PhysicsCollider3D, Vortex}, fully hero-agnostic: any spawned entity carrying those
    // three gets pull-pulses for free. Rank 2 additionally grants a real AreaDamage pulse (same
    // component/system Compression's own rank 1 uses on the Hero Skill's own vortex). Rank 3
    // "Repulsion" additionally grants VortexRepulseOnDestroy, so the Void pushes enemies away instead
    // of just expiring quietly.
    //
    // Prototype defaults to Kai's own KaiVortexEntityPrototype (same visual as his Hero Skill's
    // vortex) - assign a dedicated Dash Void prefab in the Inspector for a distinct look; nothing
    // gameplay-relevant depends on which prototype is used, as long as it carries a Vortex + a Sphere
    // PhysicsCollider3D.
    public unsafe partial class WarpWakeSkillAction : SkillActionData
    {
        [ExpandableAsset] public AssetRef<EntityPrototype> Prototype;

        public FP[] Duration = { FP.FromString("1.5"), FP.FromString("1.5"), FP.FromString("1.5") };
        public FP[] PullForce = { FP._6, FP._9, FP._9 };
        public FP[] BaseRadius = { FP.FromString("2.50"), FP.FromString("3.50"), FP.FromString("3.50") };
        public FP PullTickInterval = FP._0_50;

        // Rank 2+ only (0 at rank 1, which leaves the Void a pure pull-only field - no AreaDamage
        // granted at all).
        public FP[] PulseDamagePercent = { FP._0, FP.FromString("0.25"), FP.FromString("0.25") };
        public FP PulseTickInterval = FP._0_50;
        [ExpandableAsset] public AssetRef<HitEffectData> DamageEffect;

        // Rank 3 "Repulsion" only (0 at ranks 1-2, which leaves VortexRepulseOnDestroy ungranted).
        public FP[] RepulsionDamagePercent = { FP._0, FP._0, FP.FromString("0.75") };
        public FP[] RepulsionKnockbackForce = { FP._0, FP._0, FP._10 };

        public WarpWakeSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase firedPhase, AssetRef<SkillActionData> selfRef)
        {
            int rank = System.Math.Max(1, SkillUpgradeUtility.GetRank(f, filter.Entity, selfRef));
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            EntityRef spawned = SpawnedEntitySpawner.Spawn(f, filter.Entity, Prototype, Duration[index],
                filter.Transform3D->Position, DamageSource.Skill);

            if (f.Unsafe.TryGetPointer<Vortex>(spawned, out var vortex) == false)
            {
                Log.Error($"[Skill] {spawned} has no Vortex component - is Prototype actually a vortex-shaped entity?");
                return;
            }

            vortex->Force = PullForce[index];
            vortex->TickInterval = PullTickInterval;
            vortex->TickTimer = FP._0;

            if (f.Unsafe.TryGetPointer<PhysicsCollider3D>(spawned, out var collider) == true
                && collider->Shape.Type == Shape3DType.Sphere)
            {
                collider->Shape.Sphere.Radius = BaseRadius[index];
            }

            if (rank >= 2)
            {
                f.AddOrGet<AreaDamage>(spawned, out var area);
                area->Damage = PulseDamagePercent[index] * KaiAscensionUtility.ResolveVortexSkillDamage(f, filter.Entity);
                area->TickInterval = PulseTickInterval;
                area->TickTimer = FP._0;
                area->TargetMask = DamageTargetMask.Enemies;
                area->Effects[0] = DamageEffect;
            }

            if (rank >= 3)
            {
                f.AddOrGet<VortexRepulseOnDestroy>(spawned, out var repulse);
                repulse->Damage = RepulsionDamagePercent[index] * KaiAscensionUtility.ResolveVortexSkillDamage(f, filter.Entity);
                repulse->KnockbackForce = RepulsionKnockbackForce[index];
                repulse->Source = this;
            }
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }
    }
}
