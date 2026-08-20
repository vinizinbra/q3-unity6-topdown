namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;
    using Quantum.Physics3D;
    using UnityEngine;

    // SpawnEntityEffectData that also configures the spawned entity's AlternatingArea/AreaDamage
    // (see AlternatingAreaSystem) - so a pulsing structure's actual heal/damage numbers live with
    // the throw that places it (part of the skill's own asset chain: Hero Skill -> ProjectileData ->
    // Hit -> Effects -> this), not scattered onto a separate prototype you'd otherwise have to go
    // dig up. The prototype itself only needs to carry what's physically its own - PhysicsCollider3D
    // (the pulse shape) plus bare AlternatingArea/AreaDamage components - every value on them gets
    // overwritten here at spawn, so whatever's authored there is inert placeholder.
    public unsafe class SpawnAlternatingAreaEffectData : SpawnEntityEffectData
    {
        // Slot contract for the Support Beat's own effects list - see HealEffects below. Named
        // constants rather than bare indices so the two writers (base authoring and Sound Boost)
        // can't drift apart silently.
        internal const int SupportHealSlot = 0;
        internal const int SupportBuffSlot = 1;
        internal const int SupportCooldownSlot = 2;

        public FP TickInterval = 1;

        public DamageTargetMask HealTargetMask = DamageTargetMask.Players;

        // The SUPPORT BEAT's effects list. Slot order is a contract Sound Boost relies on (see
        // ApplySoundBoostUpgrade): [0] the heal itself (ScaledHealEffectData), [1] the ally buff
        // bundle (AllyBuffEffectData - Move Speed/Fire Rate), [2] reserved for Sound Boost rank 2+'s
        // cooldown-reduction effect, [3] free.
        public List<AssetRef<HitEffectData>> HealEffects = new();

        // Percent of a healed target's own MaxHealth (mirrors HealUtility.ApplyHeal's convention) -
        // read by ScaledHealEffectData via HitEffectContext.Damage, once AlternatingAreaSystem's
        // support-phase branch seeds AreaDamage.Damage from this. Deliberately small: the baseline
        // Support Beat is a tempo buff that also trickles health, not a heal.
        public FP HealAmount = FP.FromString("0.01");

        [Tooltip("GLOBAL cap on how much HP one Totem may ever restore to any ONE ally, as a fraction of that ally's MaxHealth. Applies at EVERY Sound Boost rank and regardless of Beat frequency, which is what stops Double Time from letting a lower Sound Boost rank out-heal a higher one. Once spent, Support Beats still deliver Move Speed / Fire Rate / cooldown reduction - only the HP half switches off.")]
        public FP MaxHealFractionPerAlly = FP._0_20;

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
            ApplyAreaMultiplier(f, context.Owner, spawned);
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

            ApplySoundBoostUpgrade(f, owner, alternating);
            ApplyAmplifierKnockback(f, owner, alternating);
            ApplyAllyBudget(f, owner, spawned);
        }

        // The per-Totem-instance spend caps (see AreaAllyBudget/AreaAllyBudgetUtility) - the whole
        // reason "20% Max HP per Totem per ally" and "N seconds of cooldown reduction per Totem per
        // ally" are properties of THIS deployable rather than of Zara, so a fresh deploy starts a
        // fresh allowance for everyone and two Zaras' Totems never share one. Always added, even with
        // no Ascension picked, since the healing cap is global by design.
        private void ApplyAllyBudget(Frame f, EntityRef owner, EntityRef spawned)
        {
            f.AddOrGet<AreaAllyBudget>(spawned, out var budget);
            budget->MaxHealFractionPerAlly = MaxHealFractionPerAlly;
            budget->MaxCooldownReductionPerAlly = f.Unsafe.TryGetPointer<SoundBoostUpgrade>(owner, out var soundBoost)
                ? soundBoost->MaxCooldownReductionPerTotem
                : FP._0;
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

        // SoundBoostUpgrade (see Heroes/Zara/SoundBoostSkillAction) - mirrors ResolveDamageAmount
        // above, on the support side, but SETS rather than scales: this line owns the Support Beat's
        // heal magnitude outright.
        private FP ResolveHealAmount(Frame f, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<SoundBoostUpgrade>(owner, out var upgrade) == true && upgrade->HealPercent > FP._0)
                return upgrade->HealPercent;

            return HealAmount;
        }

        // SoundBoostUpgrade - overwrites the Support Beat's buff slot with this rank's own authored
        // bundle, and (rank 2+) fills the reserved cooldown slot. Both are SLOT-INDEXED rather than
        // "first empty slot": the base authoring already puts a buff at [1], so appending would leave
        // two competing buff effects on the same beat and let the weaker one land as well. See
        // HealEffects' own comment for the slot contract.
        private static void ApplySoundBoostUpgrade(Frame f, EntityRef owner, AlternatingArea* alternating)
        {
            if (f.Unsafe.TryGetPointer<SoundBoostUpgrade>(owner, out var upgrade) == false)
                return;

            if (upgrade->SupportBuffEffect.IsValid == true)
            {
                alternating->HealEffects[SupportBuffSlot] = upgrade->SupportBuffEffect;
            }

            // Invalid at rank 1 - that's exactly what leaves cooldown reduction off until rank 2.
            alternating->HealEffects[SupportCooldownSlot] = upgrade->CooldownEffect;
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
        // Skill Area (CharacterStats.AreaRadiusMultiplier) - grows the whole deployed area once at
        // spawn by scaling its collider, the single radius AlternatingAreaSystem's beats, the
        // AreaDamage pulse and the ally search all read. Baked here rather than re-read per beat, the
        // same treatment (and the same reasoning) SpawnVortexEffectData gives Kai's vortex.
        // Main Stage's own RadiusBonus composes on top of this rather than instead of it.
        private static void ApplyAreaMultiplier(Frame f, EntityRef owner, EntityRef spawned)
        {
            FP multiplier = StatUtility.GetAreaMultiplier(f, owner);

            if (multiplier == FP._1 || f.Unsafe.TryGetPointer<PhysicsCollider3D>(spawned, out var collider) == false)
                return;

            switch (collider->Shape.Type)
            {
                case Shape3DType.Box:
                    collider->Shape.Box.Extents = collider->Shape.Box.Extents * multiplier;
                    break;

                case Shape3DType.Sphere:
                    collider->Shape.Sphere.Radius *= multiplier;
                    break;

                case Shape3DType.Capsule:
                    collider->Shape.Capsule.Radius *= multiplier;
                    collider->Shape.Capsule.Extent *= multiplier;
                    break;

                default:
                    Log.Error($"[Skill] {spawned} has a {collider->Shape.Type} collider - Skill Area only applies to Box, Sphere and Capsule");
                    break;
            }
        }

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
