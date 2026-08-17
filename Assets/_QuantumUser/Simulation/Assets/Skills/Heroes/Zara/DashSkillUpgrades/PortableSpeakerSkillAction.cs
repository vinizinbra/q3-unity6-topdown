namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;

    // Dash Ascension (Portable Speaker, ranked, line 2/2 on Dash) - see docs/zara-ascensions.md.
    // Absorbs Healing Step (rank 2's dash-end heal) and replaces the old, broken PortableSpeaker.asset
    // SpawnEntitySkillAction instance - unlike that generic class, this configures the spawned area's
    // AlternatingArea/AreaDamage directly (same "spawn, then hand-configure" shape
    // SpawnAlternatingAreaEffectData/Kai's WarpWakeSkillAction already use), at a fraction of the
    // Totem's own baseline Damage/Heal values so it uses the SAME alternating rhythm concept, never
    // damaging and healing simultaneously.
    public unsafe partial class PortableSpeakerSkillAction : SkillActionData
    {
        [ExpandableAsset] public AssetRef<EntityPrototype> Prototype;

        public FP[] Duration = { FP._3, FP._4, FP._4 };
        public FP BaseRadius = FP._3;
        public FP[] RadiusMultiplier = { FP._1, FP.FromString("1.30"), FP.FromString("1.30") };
        public FP BeatInterval = FP._1;

        // Mirrors the Totem's own baseline (ZaraBaseSkill/SpawnAlternatingAreaEffectData) as a
        // separately-authored constant, not a live cross-reference to that asset - simpler and fully
        // deterministic, at the cost of both needing to be kept in sync by hand during a balance pass
        // (both live side-by-side in ZaraAscensionAssetGenerator.cs to limit drift risk).
        public FP TotemBaseDamage = 10;
        public FP TotemBaseHealPercent = FP._0_10;
        public FP DamagePercentOfTotem = FP._0_50;
        public FP HealPercentOfTotem = FP._0_50;

        [ExpandableAsset] public AssetRef<HitEffectData> DamageEffect;
        [ExpandableAsset] public AssetRef<HitEffectData> HealEffect;

        // Rank 2+ - a small immediate heal to nearby allies when the dash itself ends.
        public FP[] DashEndHealPercent = { FP._0, FP._0_05, FP._0_05 };
        public FP DashEndHealRadius = FP._5;

        // Rank 3 "Mobile Stage" - fraction of Amplifier/Healing Chorus/Double Time's own bonus this
        // Speaker inherits from whichever Totem Ascensions the owner also holds.
        public FP MobileStageInheritanceFraction = FP._0_50;

        public PortableSpeakerSkillAction()
        {
            Phase = SkillActionPhase.Begin | SkillActionPhase.End;
        }

        public override FP EffectRadius => BaseRadius;

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase firedPhase, AssetRef<SkillActionData> selfRef)
        {
            int rank = System.Math.Max(1, SkillUpgradeUtility.GetRank(f, filter.Entity, selfRef));
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            if (firedPhase == SkillActionPhase.Begin)
            {
                SpawnSpeaker(f, filter.Entity, slot->StartPosition, rank, index);
                return;
            }

            if (DashEndHealPercent[index] <= FP._0)
                return;

            FPVector3 position = filter.Transform3D->Position;
            var allies = EnemyMovementUtility.FindPlayersInRadius(f, position, DashEndHealRadius);

            for (int i = 0; i < allies.Count; i++)
            {
                HealUtility.ApplyHeal(f, allies[i].Entity, filter.Entity, DashEndHealPercent[index]);
            }
        }

        private void SpawnSpeaker(Frame f, EntityRef owner, FPVector3 position, int rank, int index)
        {
            EntityRef spawned = SpawnedEntitySpawner.Spawn(f, owner, Prototype, Duration[index], position, DamageSource.Skill);

            if (f.Unsafe.TryGetPointer<AreaDamage>(spawned, out var area) == false
                || f.Unsafe.TryGetPointer<AlternatingArea>(spawned, out var alternating) == false
                || f.Unsafe.TryGetPointer<PhysicsCollider3D>(spawned, out var collider) == false)
            {
                Log.Error($"[Skill] {spawned} has no AreaDamage/AlternatingArea/PhysicsCollider3D - is Portable Speaker's Prototype actually a pulsing area?");
                return;
            }

            area->TickInterval = BeatInterval;

            // First flip must land on the Damage branch - see SpawnAlternatingAreaEffectData's own
            // identical comment on why CurrentlyHealing has to seed true, not the zeroed default.
            alternating->CurrentlyHealing = true;
            alternating->HealTargetMask = DamageTargetMask.Players;
            alternating->DamageMask = DamageTargetMask.Enemies;
            alternating->DamageAmount = TotemBaseDamage * DamagePercentOfTotem;
            alternating->HealAmount = TotemBaseHealPercent * HealPercentOfTotem;
            alternating->HealEffects[0] = HealEffect;
            alternating->DamageEffects[0] = DamageEffect;

            if (collider->Shape.Type == Shape3DType.Sphere)
            {
                collider->Shape.Sphere.Radius = BaseRadius * RadiusMultiplier[index];
            }

            // Mobile Stage (rank 3) - deliberately does NOT add MainStageBonusBeats to `spawned`, so
            // this Speaker can never fire opening/closing bonus beats regardless of the owner's own
            // Main Stage rank (see MainStage.qtn's own comment) - and never inherits Amplifier's
            // knockback/Bass-Drop-stun or Main Stage's own radius/duration either, only a fraction of
            // Amplifier's DamageBonus/Healing Chorus's HealBonus/Double Time's interval shrink.
            if (rank >= 3)
            {
                ApplyMobileStageInheritance(f, owner, alternating, area);
            }
        }

        private void ApplyMobileStageInheritance(Frame f, EntityRef owner, AlternatingArea* alternating, AreaDamage* area)
        {
            if (f.Unsafe.TryGetPointer<AmplifierUpgrade>(owner, out var amplifier) == true)
            {
                alternating->DamageAmount *= FP._1 + amplifier->DamageBonus * MobileStageInheritanceFraction;
            }

            if (f.Unsafe.TryGetPointer<HealingChorusUpgrade>(owner, out var healingChorus) == true)
            {
                alternating->HealAmount *= FP._1 + healingChorus->HealBonus * MobileStageInheritanceFraction;
            }

            if (f.Unsafe.TryGetPointer<DoubleTimeUpgrade>(owner, out var doubleTime) == true && doubleTime->BeatInterval > FP._0)
            {
                FP improvement = FP._1 - doubleTime->BeatInterval;
                area->TickInterval *= FP._1 - improvement * MobileStageInheritanceFraction;
            }
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }
    }
}
