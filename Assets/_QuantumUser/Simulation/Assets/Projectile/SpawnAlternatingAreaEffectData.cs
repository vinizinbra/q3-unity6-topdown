namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;
    using Quantum.Physics3D;

    // SpawnEntityEffectData that also configures the spawned entity's AlternatingArea/AreaDamage
    // (see AlternatingAreaSystem) - so a pulsing structure's actual heal/damage numbers live with
    // the throw that places it (part of the skill's own asset chain: Hero Skill -> ProjectileData ->
    // Hit -> Effects -> this), not scattered onto a separate prototype you'd otherwise have to go
    // dig up. The prototype itself only needs to carry what's physically its own - PhysicsCollider3D
    // (the pulse shape) plus bare AlternatingArea/AreaDamage components - every value on them gets
    // overwritten here at spawn, so whatever's authored there is inert placeholder.
    public unsafe class SpawnAlternatingAreaEffectData : SpawnEntityEffectData
    {
        public FP TickInterval = 1;

        public DamageTargetMask HealTargetMask = DamageTargetMask.Players;
        public List<AssetRef<HitEffectData>> HealEffects = new();

        // Percent of a healed target's own MaxHealth (mirrors HealUtility.ApplyHeal's convention) -
        // read by ScaledHealEffectData/OverhealToShieldEffectData via HitEffectContext.Damage, once
        // AlternatingAreaSystem's heal-phase branch seeds AreaDamage.Damage from this.
        public FP HealAmount = FP._0_10;

        public FP DamageAmount = 10;
        public DamageTargetMask DamageMask = DamageTargetMask.Enemies;
        public List<AssetRef<HitEffectData>> DamageEffects = new();

        public override void Apply(Frame f, ref HitEffectContext context)
        {
            // Main Stage rank 2+ (see MainStage.qtn) - resolved before Spawn since Duration is baked
            // in at spawn time, not re-read later.
            FP durationBonus = f.Unsafe.TryGetPointer<MainStageUpgrade>(context.Owner, out var mainStageDuration)
                ? mainStageDuration->DurationBonus
                : FP._0;

            EntityRef spawned = SpawnedEntitySpawner.Spawn(f, context.Owner, Prototype, Duration + durationBonus, context.Position, context.Source, context.Element);
            Configure(f, context.Owner, spawned);
            ApplyMainStageRadius(f, context.Owner, spawned);

            // Main Stage rank 3 "Main Stage" - an immediate opening Damage Beat on successful
            // deployment, benefiting from Amplifier/whatever else Configure just baked in above.
            // MainStageBonusBeats is stamped ONLY here, on the entity THIS call just spawned - never
            // on the owner - see MainStage.qtn's own comment on why that distinction is load-bearing
            // for keeping Portable Speaker excluded.
            if (f.Unsafe.TryGetPointer<MainStageUpgrade>(context.Owner, out var mainStageRank) == true && mainStageRank->Rank >= 3)
            {
                f.Add<MainStageBonusBeats>(spawned);
                AlternatingAreaSystem.FireBonusPulse(f, spawned, isHealing: false);
            }
        }

        private void Configure(Frame f, EntityRef owner, EntityRef spawned)
        {
            if (f.Unsafe.TryGetPointer<AreaDamage>(spawned, out var area) == true)
            {
                area->TickInterval = ResolveTickInterval(f, owner);
            }

            if (f.Unsafe.TryGetPointer<AlternatingArea>(spawned, out var alternating) == false)
                return;

            // The very first flip must resolve to the Damage branch (spec: "Default sequence begins
            // with Damage Beat") - AlternatingAreaSystem.Update computes healNext as (CurrentlyHealing
            // == false), so seeding true here (rather than the zeroed default false) is what makes
            // that first flip land on false (damage) instead of true (heal).
            alternating->CurrentlyHealing = true;

            alternating->HealTargetMask = HealTargetMask;
            CopyEffects(HealEffects, alternating->HealEffects);
            alternating->HealAmount = ResolveHealAmount(f, owner);

            alternating->DamageAmount = ResolveDamageAmount(f, owner);
            alternating->DamageMask = DamageMask;
            CopyEffects(DamageEffects, alternating->DamageEffects);

            ApplyHealingChorusUpgrade(f, owner, alternating);
            ApplyAmplifierKnockback(f, owner, alternating);
        }

        // DoubleTimeUpgrade (see Heroes/Zara/DoubleTimeSkillAction) - a direct interval override
        // (not a rate multiplier, spec pins exact seconds per rank) read once at spawn, same
        // bake-once shape TickRateUpgrade used before it.
        private FP ResolveTickInterval(Frame f, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<DoubleTimeUpgrade>(owner, out var upgrade) == true && upgrade->BeatInterval > FP._0)
                return upgrade->BeatInterval;

            return TickInterval;
        }

        // AmplifierUpgrade (see Heroes/Zara/AmplifierSkillAction) - boosts the amount rather than the
        // caller supplying a pre-boosted value, same shape ResolveTickInterval uses.
        private FP ResolveDamageAmount(Frame f, EntityRef owner)
        {
            FP bonus = f.Unsafe.TryGetPointer<AmplifierUpgrade>(owner, out var upgrade) == true ? upgrade->DamageBonus : FP._0;
            return DamageAmount * (FP._1 + bonus);
        }

        // HealingChorusUpgrade (see Heroes/Zara/HealingChorusSkillAction) - mirrors
        // ResolveDamageAmount above, on the heal side.
        private FP ResolveHealAmount(Frame f, EntityRef owner)
        {
            FP bonus = f.Unsafe.TryGetPointer<HealingChorusUpgrade>(owner, out var upgrade) == true ? upgrade->HealBonus : FP._0;
            return HealAmount * (FP._1 + bonus);
        }

        // HealingChorusUpgrade - overwrites HealEffects[0] with whichever heal-effect asset the
        // current rank calls for (ScaledHealEffectData at rank 1-2, OverhealToShieldEffectData at
        // rank 3 "Encore") rather than appending, since this replaces the base heal magnitude itself,
        // not a bonus layered on top of it. HasteEffect (rank 2+) is still appended into the first
        // open slot, same bake-once-at-spawn shape the old HasteOnHealUpgrade used.
        private static void ApplyHealingChorusUpgrade(Frame f, EntityRef owner, AlternatingArea* alternating)
        {
            if (f.Unsafe.TryGetPointer<HealingChorusUpgrade>(owner, out var upgrade) == false)
                return;

            if (upgrade->HealEffectAsset.IsValid == true)
            {
                alternating->HealEffects[0] = upgrade->HealEffectAsset;
            }

            if (upgrade->HasteEffect.IsValid == false)
                return;

            for (int i = 0; i < alternating->HealEffects.Length; i++)
            {
                if (alternating->HealEffects[i].IsValid == true)
                    continue;

                alternating->HealEffects[i] = upgrade->HasteEffect;

                Log.Debug($"[Skill] {owner}'s HealingChorusUpgrade baked Haste into the spawned area's HealEffects slot {i}");
                return;
            }

            Log.Error($"[Skill] {owner}'s HealingChorusUpgrade couldn't fit Haste - the spawned area's HealEffects already fills all 4 slots");
        }

        // AmplifierUpgrade rank 2+ - appended into DamageEffects, same bake-once-at-spawn shape the
        // old KnockbackOnDamageUpgrade used - every Damage Beat should knock back, not just some of
        // them, so there's nothing conditional to check live the way Bass Drop's stun needs.
        private static void ApplyAmplifierKnockback(Frame f, EntityRef owner, AlternatingArea* alternating)
        {
            if (f.Unsafe.TryGetPointer<AmplifierUpgrade>(owner, out var upgrade) == false || upgrade->KnockbackEffect.IsValid == false)
                return;

            for (int i = 0; i < alternating->DamageEffects.Length; i++)
            {
                if (alternating->DamageEffects[i].IsValid == true)
                    continue;

                alternating->DamageEffects[i] = upgrade->KnockbackEffect;

                Log.Debug($"[Skill] {owner}'s AmplifierUpgrade baked knockback into the spawned area's DamageEffects slot {i}");
                return;
            }

            Log.Error($"[Skill] {owner}'s AmplifierUpgrade couldn't fit knockback - the spawned area's DamageEffects already fills all 4 slots");
        }

        // MainStageUpgrade.RadiusBonus - deliberately NOT the shared, cross-hero SpawnRadiusUpgrade
        // (see MainStage.qtn's own comment on why) - same per-shape scaling math
        // SpawnedEntitySpawner.ApplyRadiusUpgrade uses, just reading this Zara-specific field so it
        // only ever affects a Totem, never leaking onto a Portable Speaker spawned through the same
        // SpawnedEntitySpawner.Spawn call.
        private static void ApplyMainStageRadius(Frame f, EntityRef owner, EntityRef spawned)
        {
            if (f.Unsafe.TryGetPointer<MainStageUpgrade>(owner, out var upgrade) == false || upgrade->RadiusBonus <= FP._0)
                return;

            if (f.Unsafe.TryGetPointer<PhysicsCollider3D>(spawned, out var collider) == false)
                return;

            FP scale = FP._1 + upgrade->RadiusBonus;

            switch (collider->Shape.Type)
            {
                case Shape3DType.Box:
                    collider->Shape.Box.Extents = collider->Shape.Box.Extents * scale;
                    break;

                case Shape3DType.Sphere:
                    collider->Shape.Sphere.Radius *= scale;
                    break;

                case Shape3DType.Capsule:
                    collider->Shape.Capsule.Radius *= scale;
                    collider->Shape.Capsule.Extent *= scale;
                    break;

                default:
                    Log.Error($"[Skill] {spawned} has a {collider->Shape.Type} collider - Main Stage's RadiusBonus only applies to Box, Sphere and Capsule");
                    break;
            }
        }

        // destination is a FixedArray "handle" into the entity's own component memory (same as
        // AlternatingAreaSystem.CopyEffects) - indexing it writes through to the real data even
        // though it's passed by value, so no ref is needed.
        private static void CopyEffects(List<AssetRef<HitEffectData>> source, FixedArray<AssetRef<HitEffectData>> destination)
        {
            for (int i = 0; i < destination.Length; i++)
            {
                destination[i] = i < source.Count ? source[i] : default;
            }
        }
    }
}
