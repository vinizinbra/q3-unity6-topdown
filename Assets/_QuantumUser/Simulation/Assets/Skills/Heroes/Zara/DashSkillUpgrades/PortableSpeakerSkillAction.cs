namespace Quantum
{
    using System;
    using Photon.Deterministic;
    using Quantum.Physics3D;
    using UnityEngine;

    // Ranked Dash Ascension (Portable Speaker, line 2/2 on Dash) - Zara's dash leaves behind a small,
    // short-lived mobile support device running the SAME alternating Damage -> Support rhythm her
    // Totem does, at a reduced profile. It configures the spawned area's AlternatingArea/AreaDamage
    // directly (the same "spawn, then hand-configure" shape SpawnAlternatingAreaEffectData uses)
    // rather than being a second beat mechanism.
    //
    //  - Rank 1: leaves a Speaker (short duration, small radius, reduced Damage/Support beats).
    //  - Rank 2: longer, wider, and completing the dash also gives nearby allies a short Support-style
    //    buff directly.
    //  - Rank 3 "Mobile Stage": inherits Zara's own Beat-interval and radius modifiers, and runs the
    //    reduced-effectiveness variants of whatever Sound Boost profile she holds.
    //
    // A Speaker NEVER heals HP, at any rank - by construction, not by a runtime check: nothing here
    // ever writes a heal effect into its Support Beat slot, and HealAmount stays 0. That also means it
    // needs no AreaAllyBudget (there is no HP for a per-Totem cap to govern), and it never inherits
    // Main Stage's opening/closing bonus beats (MainStageBonusBeats is only ever stamped by the
    // Totem's own spawn path).
    //
    // Active Speakers are capped per Zara (MaxActiveSpeakers) - a new one past the cap silently
    // retires the oldest via DespawnIntentUtility (reason Replaced), so no on-destroy effect misreads
    // housekeeping as a death. Counting is scoped by AreaOwner.Owner, so two Zaras never share a cap.
    public unsafe partial class PortableSpeakerSkillAction : SkillActionData
    {
        [ExpandableAsset] public AssetRef<EntityPrototype> Prototype;

        public FP[] Duration = { FP._3, FP._4, FP._4 };
        public FP BaseRadius = FP._3;
        public FP[] RadiusMultiplier = { FP._1, FP.FromString("1.30"), FP.FromString("1.30") };
        public FP BeatInterval = FP._1;

        [Tooltip("How many Speakers one Zara may have live at once, per rank. Rank 3 optionally raises it - a new Speaker past the cap retires her oldest.")]
        public byte[] MaxActiveSpeakers = { 1, 1, 2 };

        // Mirrors the Totem's own baseline (ZaraBaseSkill/SpawnAlternatingAreaEffectData) as a
        // separately-authored constant, not a live cross-reference to that asset - simpler and fully
        // deterministic, at the cost of both needing to be kept in sync by hand during a balance pass
        // (both live side-by-side in ZaraAscensionAssetGenerator.cs to limit drift risk).
        public FP TotemBaseDamage = 10;
        public FP DamagePercentOfTotem = FP._0_50;

        [ExpandableAsset] public AssetRef<HitEffectData> DamageEffect;

        [Tooltip("The Speaker's own baseline Support Beat buff (reduced Move Speed / Fire Rate, never healing). Replaced by Sound Boost's own Speaker variant at rank 3 if she holds that line.")]
        [ExpandableAsset] public AssetRef<HitEffectData> SupportBuffEffect;

        [Header("Rank 2 - dash-end buff")]
        [Tooltip("Applied directly to nearby allies when the dash completes. A buff, never a heal.")]
        [ExpandableAsset] public AssetRef<HitEffectData> DashEndBuffEffect;
        public FP DashEndBuffRadius = FP._5;

        [Header("Rank 3 - Mobile Stage")]
        [Tooltip("Fraction of Zara's own Double Time interval-shrink and Main Stage radius bonus the Speaker inherits. Sound Boost is inherited through its own authored Speaker-variant assets instead, not through this fraction.")]
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
                RetireOldestOverCap(f, filter.Entity, MaxActiveSpeakers[index]);
                SpawnSpeaker(f, filter.Entity, slot->StartPosition, rank, index);
                return;
            }

            // Rank 2+ - the dash's own completion buff. Deliberately routed through the same generic
            // AllyBuffEffectData asset the beats use, so "a short Support-style combat buff" is
            // literally the same data, not a parallel implementation.
            if (rank < 2 || DashEndBuffEffect.IsValid == false)
                return;

            // IncludingDashing, not the plain FindPlayersInRadius: this fires at dash END, when Zara
            // herself is still on the IgnoreProjectile layer as far as this tick's already-built
            // broadphase is concerned, so a plain Player-mask query would buff every ally EXCEPT the
            // one who earned it. Same fix, same reason, as Brute's Bodyguard - see that class.
            FPVector3 position = filter.Transform3D->Position;
            Span<EntityRef> allies = stackalloc EntityRef[PlayerQueryUtility.MaxPlayerLayerCandidates];
            int alliesCount = EnemyMovementUtility.FindPlayersInRadiusIncludingDashing(f, position, DashEndBuffRadius, allies);
            HitEffectData buff = f.FindAsset(DashEndBuffEffect);

            for (int i = 0; i < alliesCount; i++)
            {
                var context = new HitEffectContext
                {
                    Owner = filter.Entity,
                    Target = allies[i],
                    Position = position,
                    PushDirection = FPVector3.Zero,
                    Damage = FP._0,
                    Source = DamageSource.Skill,
                    Element = ElementType.Neutral,
                };

                buff.Apply(f, ref context);
            }
        }

        // Retires this Zara's oldest live Speaker(s) until spawning one more will stay within the cap.
        // "Oldest" is the smallest DestroyAfterTime.RemainingTime, which is deterministic and needs no
        // extra bookkeeping. Retired via DespawnIntentUtility so the removal is tagged Replaced rather
        // than reading as a destruction to any on-destroy hook.
        private const int MaxTrackedSpeakers = 8;

        private static void RetireOldestOverCap(Frame f, EntityRef owner, byte maxActive)
        {
            if (maxActive == 0)
                return;

            // Collected in ONE pass, then retired - deliberately not a "count, retire one, re-count"
            // loop, which would depend on f.Destroy being observable to a fresh filter query in the
            // same tick. At most a handful of these ever exist, so a small fixed-size scratch array is
            // simpler than any ordered bookkeeping on the owner.
            EntityRef* entities = stackalloc EntityRef[MaxTrackedSpeakers];
            FP* remaining = stackalloc FP[MaxTrackedSpeakers];
            int count = 0;

            var speakers = f.Filter<PortableSpeaker, AreaOwner, DestroyAfterTime>();

            while (speakers.Next(out EntityRef entity, out PortableSpeaker _, out AreaOwner areaOwner, out DestroyAfterTime lifetime))
            {
                if (areaOwner.Owner != owner || count >= MaxTrackedSpeakers)
                    continue;

                entities[count] = entity;
                remaining[count] = lifetime.RemainingTime;
                count++;
            }

            // How many have to go so that spawning one more lands exactly at the cap.
            int excess = count - maxActive + 1;

            for (int i = 0; i < excess; i++)
            {
                // Smallest RemainingTime = the one closest to expiring = the oldest still-live
                // Speaker. Deterministic and needs no spawn-order bookkeeping.
                int oldest = -1;
                FP lowest = FP.MaxValue;

                for (int j = 0; j < count; j++)
                {
                    if (entities[j] == EntityRef.None || remaining[j] >= lowest)
                        continue;

                    lowest = remaining[j];
                    oldest = j;
                }

                if (oldest < 0)
                    return;

                DespawnIntentUtility.DespawnSilently(f, entities[oldest], EntityDespawnReason.Replaced);
                entities[oldest] = EntityRef.None;
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

            f.AddOrGet<PortableSpeaker>(spawned, out _);

            area->TickInterval = BeatInterval;

            // First flip must land on the Damage branch - see SpawnAlternatingAreaEffectData's own
            // identical comment on why CurrentlyHealing has to seed true, not the zeroed default.
            alternating->CurrentlyHealing = true;
            alternating->HealTargetMask = DamageTargetMask.Players;
            alternating->DamageMask = DamageTargetMask.Enemies;
            alternating->DamageAmount = TotemBaseDamage * DamagePercentOfTotem;

            // 0, always - a Speaker has no HP healing at any rank, so there is nothing for
            // ScaledHealEffectData to scale even if one were ever authored into its list.
            alternating->HealAmount = FP._0;
            alternating->DamageEffects[0] = DamageEffect;

            // Support Beat = buff only. Slot 1 mirrors the Totem's own slot contract (see
            // SpawnAlternatingAreaEffectData.SupportBuffSlot) purely for readability - slot 0 is left
            // empty rather than holding a heal.
            alternating->HealEffects[SpawnAlternatingAreaEffectData.SupportBuffSlot] = SupportBuffEffect;

            // Skill Area (CharacterStats.AreaRadiusMultiplier) - see StatUtility.GetAreaMultiplier. A Speaker is a deployed area like the
            // Totem it is a mini version of, so it scales with Skill Area for the same reason.
            FP radius = BaseRadius * RadiusMultiplier[index] * StatUtility.GetAreaMultiplier(f, owner);

            if (rank >= 3)
            {
                ApplyMobileStageInheritance(f, owner, alternating, area, ref radius);
            }

            if (collider->Shape.Type == Shape3DType.Sphere)
            {
                collider->Shape.Sphere.Radius = radius;
            }
        }

        // Mobile Stage (rank 3) - the simplified inheritance rules the brief asks for. It picks up
        // Zara's TEMPO and REACH modifiers (Double Time's interval shrink, Main Stage's radius) at
        // MobileStageInheritanceFraction, and swaps in Sound Boost's own authored reduced-effect
        // Speaker variants for the buff/cooldown halves.
        //
        // It deliberately does NOT inherit: HP healing (a Speaker has none), the per-Totem healing cap
        // (nothing to cap), Main Stage's opening/closing bonus beats (MainStageBonusBeats is never
        // stamped here), Main Stage's duration bonus, or Amplifier's knockback/Bass-Drop stun. The
        // Damage Beat DOES pick up Amplifier's damage bonus, since Amplifier is explicitly listed as
        // inheritable.
        private void ApplyMobileStageInheritance(Frame f, EntityRef owner, AlternatingArea* alternating, AreaDamage* area, ref FP radius)
        {
            if (f.Unsafe.TryGetPointer<AmplifierUpgrade>(owner, out var amplifier) == true)
            {
                alternating->DamageAmount *= FP._1 + amplifier->DamageBonus * MobileStageInheritanceFraction;
            }

            if (f.Unsafe.TryGetPointer<SoundBoostUpgrade>(owner, out var soundBoost) == true)
            {
                if (soundBoost->SpeakerSupportBuffEffect.IsValid == true)
                {
                    alternating->HealEffects[SpawnAlternatingAreaEffectData.SupportBuffSlot] = soundBoost->SpeakerSupportBuffEffect;
                }

                // Invalid below Sound Boost rank 2 - that's what leaves cooldown reduction off.
                alternating->HealEffects[SpawnAlternatingAreaEffectData.SupportCooldownSlot] = soundBoost->SpeakerCooldownEffect;
            }

            if (f.Unsafe.TryGetPointer<DoubleTimeUpgrade>(owner, out var doubleTime) == true && doubleTime->BeatInterval > FP._0)
            {
                FP improvement = FP._1 - doubleTime->BeatInterval;
                area->TickInterval *= FP._1 - improvement * MobileStageInheritanceFraction;
            }

            if (f.Unsafe.TryGetPointer<MainStageUpgrade>(owner, out var mainStage) == true && mainStage->RadiusBonus > FP._0)
            {
                radius *= FP._1 + mainStage->RadiusBonus * MobileStageInheritanceFraction;
            }
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }
    }
}
