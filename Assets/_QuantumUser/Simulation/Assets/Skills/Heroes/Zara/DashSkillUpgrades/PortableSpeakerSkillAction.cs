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
    // A Speaker DOES heal, at HealPercentOfTotem (half) of whatever the Totem's own Support Beat would
    // currently restore - so it tracks Sound Boost's rank automatically rather than needing its own
    // ladder. It used to heal nothing at all by construction; that was reversed once Shield stopped
    // being a universal defensive currency and healing became the thing Zara's kit actually trades in.
    //
    // Because it heals, it carries its own AreaAllyBudget exactly like the Totem does - see
    // MaxHealFractionPerAlly below for why that cap is load-bearing rather than belt-and-braces.
    //
    // It still never inherits Main Stage's opening/closing bonus beats (MainStageBonusBeats is only
    // ever stamped by the Totem's own spawn path).
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

        [Tooltip("The Totem's own baseline Support Beat heal (SpawnAlternatingAreaEffectData.HealAmount), mirrored here for the same reason TotemBaseDamage is. Only used when Zara holds no Sound Boost - with it, her live per-rank value is read instead.")]
        public FP TotemBaseHeal = FP.FromString("0.01");

        [ExpandableAsset] public AssetRef<HitEffectData> DamageEffect;

        [Tooltip("The Speaker's own baseline Support Beat buff (reduced Move Speed / Fire Rate). Replaced by Sound Boost's own Speaker variant at rank 3 if she holds that line.")]
        [ExpandableAsset] public AssetRef<HitEffectData> SupportBuffEffect;

        [Tooltip("The Support Beat's heal. Author the SAME ScaledHealEffectData the Totem uses - it reads its percentage from AlternatingArea.HealAmount, which is halved below, so one shared asset covers both and there is no second number to keep in sync. Left unassigned, the Speaker simply doesn't heal.")]
        [ExpandableAsset] public AssetRef<HitEffectData> HealEffect;

        [Tooltip("Fraction of the Totem's CURRENT Support Beat heal that a Speaker restores. Resolved live off Zara's own Sound Boost rank, so the Speaker tracks that line automatically instead of carrying its own heal ladder that could drift out of sync.")]
        public FP HealPercentOfTotem = FP._0_50;

        [Tooltip("Per-Speaker cap on how much HP one Speaker may ever restore to any ONE ally, as a fraction of their MaxHealth - the same mechanism (and the same reason) as the Totem's own MaxHealFractionPerAlly. Load-bearing rather than belt-and-braces: a rank-3 Speaker inherits Double Time's shorter Beat interval, so without a cap more beats would let a lower Sound Boost rank out-heal a higher one, which is exactly the failure the Totem's cap exists to prevent. Half the Totem's 20% by default, matching the halved heal.")]
        public FP MaxHealFractionPerAlly = FP._0_10;

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

            // Half (HealPercentOfTotem) of whatever the Totem's own Support Beat would currently heal,
            // resolved live off Zara's Sound Boost rank - so the Speaker follows that line up its
            // ladder automatically instead of carrying a second set of per-rank numbers to keep in
            // sync. ScaledHealEffectData reads this as a percentage of the target's own MaxHealth.
            alternating->HealAmount = ResolveTotemHealAmount(f, owner) * HealPercentOfTotem;
            alternating->DamageEffects[0] = DamageEffect;

            // Support Beat slot contract, mirroring the Totem's own (see
            // SpawnAlternatingAreaEffectData.SupportHealSlot/SupportBuffSlot): [0] the heal, [1] the
            // buff bundle. Slot 0 holds the SAME ScaledHealEffectData asset the Totem uses - it takes
            // its percentage from HealAmount above, so halving that is the entire difference and there
            // is no parallel heal asset to keep in step.
            alternating->HealEffects[SpawnAlternatingAreaEffectData.SupportHealSlot] = HealEffect;
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

            ApplyAllyBudget(f, owner, spawned);

            // Same reasoning as the Totem's own spawn path - a Speaker dropped while she is already at
            // Max Flow must pick up Headliner rank 2 straight away, not wait for the next transition.
            alternating->EffectivenessMultiplier = FP._1;
            ZaraFlowUtility.RefreshOwnedAreaEffectiveness(f, owner);
        }

        // Per-SPEAKER spend caps, exactly the shape the Totem uses (see
        // SpawnAlternatingAreaEffectData.ApplyAllyBudget) - a property of THIS deployable, so a fresh
        // drop is a fresh allowance for everyone and two Zaras' Speakers never share one.
        //
        // Always added, even at rank 1 with no Ascension: the healing cap is global by design. A rank-3
        // Speaker inherits Double Time's shorter Beat interval, so without a cap more beats would let a
        // lower Sound Boost rank out-heal a higher one - the exact failure the Totem's own cap exists
        // to prevent.
        //
        // Cooldown reduction is inherited only through Sound Boost's authored Speaker-variant effect
        // (see ApplyMobileStageInheritance), so its allowance rides on the same per-Totem number rather
        // than getting a second one of its own.
        private void ApplyAllyBudget(Frame f, EntityRef owner, EntityRef spawned)
        {
            f.AddOrGet<AreaAllyBudget>(spawned, out var budget);

            budget->MaxHealFractionPerAlly = MaxHealFractionPerAlly;
            budget->MaxCooldownReductionPerAlly = f.Unsafe.TryGetPointer<SoundBoostUpgrade>(owner, out var soundBoost)
                ? soundBoost->MaxCooldownReductionPerTotem
                : FP._0;
        }

        // The Totem's CURRENT Support Beat heal - Sound Boost's own per-rank value if Zara holds that
        // line, else the Totem's baseline. Mirrors SpawnAlternatingAreaEffectData.ResolveHealAmount
        // (which is private to that asset) rather than cross-referencing it live, the same deliberate
        // trade TotemBaseDamage already makes above: simpler and fully deterministic, at the cost of
        // TotemBaseHeal needing to be kept in step by hand during a balance pass. Both live
        // side-by-side in ZaraAscensionAssetGenerator.cs to limit that drift risk.
        private FP ResolveTotemHealAmount(Frame f, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<SoundBoostUpgrade>(owner, out var upgrade) == true && upgrade->HealPercent > FP._0)
                return upgrade->HealPercent;

            return TotemBaseHeal;
        }

        // Mobile Stage (rank 3) - the simplified inheritance rules the brief asks for. It picks up
        // Zara's TEMPO and REACH modifiers (Double Time's interval shrink, Main Stage's radius) at
        // MobileStageInheritanceFraction, and swaps in Sound Boost's own authored reduced-effect
        // Speaker variants for the buff/cooldown halves.
        //
        // It deliberately does NOT inherit: Main Stage's opening/closing bonus beats
        // (MainStageBonusBeats is never stamped here), Main Stage's duration bonus, or Amplifier's
        // knockback/Bass-Drop stun. The Damage Beat DOES pick up Amplifier's damage bonus, since
        // Amplifier is explicitly listed as inheritable.
        //
        // HP healing is NOT inherited here either, but for a different reason than the rest: a Speaker
        // heals at every rank now (HealPercentOfTotem of the Totem's live value, resolved in
        // SpawnSpeaker), so there is nothing left for Mobile Stage to add. Its cap is likewise its own
        // (MaxHealFractionPerAlly), not the Totem's.
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
