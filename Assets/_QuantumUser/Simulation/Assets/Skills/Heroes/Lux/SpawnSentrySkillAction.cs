namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Dedicated spawn action (not the generic SpawnEntitySkillAction) because it needs to configure
    // the spawned sentry's Sentry/Weapon/aura fields in the same method call that has the entity
    // reference - SkillSlot has nowhere safe to stash "the entity I just spawned" for a second action
    // to pick up later (an EntityRef field on SkillSlot silently breaks the Upgrades array's own
    // Inspector authoring - CharacterSkills.qtn's own SkillSlot.ProjectilePending comment documents
    // exactly this). Mirrors SpawnVortexEffectData's own shape: spawn, then read each Begin-only
    // upgrade off the caster and bake a copy onto the spawned entity, so the sentry keeps working even
    // once the caster's own activation has long since ended.
    //
    // BASELINE IS DELIBERATELY MINIMAL - one Cannon, short range, no shield, no aura, no on-death
    // explosion, no fire-rate bonus. Rocket/Minigun/Laser/Shield Battery/Fire Support/Overclock/
    // Extended Range/Overload Core were all removed from the baseline and are Ascensions now; that arc
    // from "a basic machine" to "a full weapons platform" is the entire point of Lux's redesign.
    //
    // Priority deliberately high (opposite of IncreaseAreaSkillAction's -100) - this lives in the
    // skill's baseline Actions, while every Ascension's grant lives in slot->Upgrades, and
    // SkillSystem.InvokeActions runs baseline Actions before Upgrades whenever Priority ties (both
    // default to 0). Left at the default, this would read each upgrade's component BEFORE that same
    // activation's own grant action (re)wrote it, so a sentry deployed the very first time an upgrade
    // was picked would spawn without it. A high Priority forces this to run last within the phase.
    public unsafe partial class SpawnSentrySkillAction : SkillActionData
    {
        public AssetRef<EntityPrototype> Prototype;

        // Transform3D + InputSource + SentryBarrel - Weapon is added dynamically per armed slot (see
        // ApplyWeaponUpgrade), not pre-authored, since a sentry arms anywhere from 1 to 4 of them.
        public AssetRef<EntityPrototype> BarrelPrototype;

        // No longer a hard despawn timer - see ResolveDecayRate, which turns this into how long a
        // fully-undamaged sentry survives its own Health drain instead.
        public FP Duration = 10;

        [Tooltip("Baseline targeting/aura range. Fortification rank 1 (Extended Range) adds to it.")]
        public FP Range = 3;

        [Tooltip("The percentage basis every Lux Ascension that deals damage scales off - see LuxAscensionUtility.ResolveSentrySkillDamage.")]
        public FP SkillDamage = 20;

        [Header("Baseline armament")]
        [Tooltip("The Cannon every sentry always deploys with, in slot 0. Weapon Systems arms slots 1-3 on top; Field Modifications rank 3 (MK II) swaps THIS one for a Twin Cannon.")]
        [ExpandableAsset] public AssetRef<WeaponDataAsset> BaselineWeapon;
        public FPVector3 BaselineWeaponOffset = new FPVector3(0, FP._0_50, 0);

        // Local-space (X=right, Y=up, Z=forward), rotated by the caster's own aim yaw - same
        // convention SpawnEntitySkillAction's own Offset uses.
        public FPVector3 Offset = new FPVector3(0, 0, 2);

        public SpawnSentrySkillAction()
        {
            Phase = SkillActionPhase.Begin;
            Priority = 100;
        }

        // {0} = Range, {1} = Duration - both flat values (not percents).
        protected override object[] DescriptionArgs => new object[] { Range, Duration };

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Enforced BEFORE spawning, so the cap is a genuine ceiling on live machines rather than
            // something that briefly exceeds it. Retiring past-cap sentries is deliberately silent
            // (DespawnIntent reason Replaced) - Overload Core must not pay out for housekeeping, or
            // redeploy-spam becomes the optimal way to fire it.
            RetireOldestOverCap(f, filter.Entity);

            // Aim only decides WHERE this spawns (in front of Lux's current facing) - not how it's
            // oriented. The chassis keeps whatever rotation Prototype was authored with instead of
            // snapping to her aim.
            FPQuaternion aimFacing = FPQuaternion.Euler(0, filter.Aim->Angle, 0);
            FPVector3 position = filter.Transform3D->Position + aimFacing * Offset;

            FP duration = Duration + ResolveDurationBonus(f, filter.Entity);
            EntityRef spawned = SpawnedEntitySpawner.Spawn(f, filter.Entity, Prototype, duration, position, DamageSource.Skill);

            FPQuaternion chassisRotation = FPQuaternion.Identity;

            if (f.Unsafe.TryGetPointer<Transform3D>(spawned, out var transform) == true)
            {
                chassisRotation = transform->Rotation;
            }

            if (f.Unsafe.TryGetPointer<Sentry>(spawned, out var sentry) == false)
            {
                Log.Error($"[Skill] {spawned} has no Sentry component - is Prototype actually the sentry gun?");
                return;
            }

            // Skill Area scales the deployed turret's reach, the same way it already scales every other
            // skill's area in the game (HitPathSkillAction/SpawnEntitySkillAction/AreaHitData all
            // compose slot->AreaMultiplier with StatUtility.GetAreaMultiplier exactly like this).
            // Sentry.Range is the single value that drives targeting range, the Fortification aura's
            // own reach (Range * AuraRangeRatio) and the range indicator ring, so scaling it here is
            // all that's needed - every consumer reads it live.
            //
            // Applied AFTER Fortification rank 1's additive Extended Range bonus, so the percentage
            // covers the ascension's contribution too rather than only the authored baseline - same
            // ordering Brute's Aftershock uses for its own stack-radius bonus.
            sentry->Range = (Range + ResolveRangeBonus(f, filter.Entity))
                * slot->AreaMultiplier * StatUtility.GetAreaMultiplier(f, filter.Entity);
            sentry->Owner = filter.Entity;
            sentry->DecayRate = ResolveDecayRate(f, spawned);
            sentry->TempFireRateMultiplier = FP._1;
            sentry->TempFireRateRemaining = FP._0;
            sentry->RedlineActive = false;

            // Per-sentry extension allowance - 0 unless a Dash Ascension rank that offers extensions
            // is held (see SentryLifetimeExtensionBudget), which is what keeps a dash-cooldown build
            // from holding one machine open indefinitely.
            sentry->LifetimeExtensionRemaining = f.Unsafe.TryGetPointer<SentryLifetimeExtensionBudget>(filter.Entity, out var budget)
                ? budget->MaxPerSentry
                : FP._0;

            ApplyOverclockUpgrade(f, filter.Entity, sentry);
            ApplyBaselineWeapon(f, filter.Entity, spawned, position, chassisRotation);
            ApplyWeaponUpgrade(f, filter.Entity, spawned, position, chassisRotation);
            ApplyFortificationUpgrade(f, filter.Entity, spawned);
            ApplyOverloadUpgrade(f, filter.Entity, spawned);
            ApplyFieldModifications(f, filter.Entity, spawned);

            Log.Debug($"[Skill] {filter.Entity} deployed a sentry {spawned} at {position}, range {sentry->Range}, duration {duration}");
        }

        // Deploying past LuxScrapCollector.MaxActiveSentries retires this Lux's oldest live sentries
        // first. Collected in ONE pass then retired - deliberately not a "count, retire one, re-count"
        // loop, which would depend on f.Destroy being observable to a fresh filter query in the same
        // tick. Scoped by Sentry.Owner, so two Luxes never count against each other.
        private const int MaxTrackedSentries = 8;

        private static void RetireOldestOverCap(Frame f, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<LuxScrapCollector>(owner, out var collector) == false || collector->MaxActiveSentries == 0)
                return;

            EntityRef* entities = stackalloc EntityRef[MaxTrackedSentries];
            FP* remaining = stackalloc FP[MaxTrackedSentries];
            int count = 0;

            var sentries = f.Filter<Sentry, Health>();

            while (sentries.Next(out EntityRef entity, out Sentry sentry, out Health health))
            {
                if (sentry.Owner != owner || count >= MaxTrackedSentries)
                    continue;

                entities[count] = entity;
                remaining[count] = sentry.DecayRate > FP._0 ? health.CurrentHealth / sentry.DecayRate : FP.MaxValue;
                count++;
            }

            int excess = count - collector->MaxActiveSentries + 1;

            for (int i = 0; i < excess; i++)
            {
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

        private static FP ResolveDurationBonus(Frame f, EntityRef owner)
        {
            return f.Unsafe.TryGetPointer<SentryOverclockUpgrade>(owner, out var upgrade) ? upgrade->DurationBonus : FP._0;
        }

        // Fortification rank 1 "Extended Range" - additive on top of this action's own authored Range.
        private static FP ResolveRangeBonus(Frame f, EntityRef owner)
        {
            return f.Unsafe.TryGetPointer<SentryFortificationUpgrade>(owner, out var upgrade) ? upgrade->RangeBonus : FP._0;
        }

        // Turns the resolved duration into Sentry.DecayRate (MaxHealth / duration) instead of a plain
        // DestroyAfterTime countdown - see SentryDecaySystem and Sentry.qtn's own DecayRate comment.
        // SpawnedEntitySpawner.Spawn already added DestroyAfterTime with the fully-resolved duration
        // (including any IncreaseDurationUpgrade stretch); read that value once here for the decay
        // math, then remove the component so the generic timer doesn't ALSO destroy this entity
        // independently of Health.
        private static FP ResolveDecayRate(Frame f, EntityRef spawned)
        {
            FP resolvedDuration = FP._0;

            if (f.Unsafe.TryGetPointer<DestroyAfterTime>(spawned, out var lifetime) == true)
            {
                resolvedDuration = lifetime->RemainingTime;
                f.Remove<DestroyAfterTime>(spawned);
            }

            if (resolvedDuration <= FP._0)
            {
                Log.Error($"[Skill] {spawned}'s resolved duration is {resolvedDuration} - leaving DecayRate at 0 instead of dividing by it");
                return FP._0;
            }

            if (f.Unsafe.TryGetPointer<Health>(spawned, out var health) == false || health->MaxHealth <= FP._0)
            {
                Log.Error($"[Skill] {spawned} has no Health (or MaxHealth 0) authored - is Prototype missing Health? Decay disabled");
                return FP._0;
            }

            return health->MaxHealth / resolvedDuration;
        }

        // Overclock - the permanent fire-rate multiplier plus rank 3's Redline configuration. Read
        // once at deploy; SentryDecaySystem is what later latches RedlineActive on.
        private static void ApplyOverclockUpgrade(Frame f, EntityRef owner, Sentry* sentry)
        {
            if (f.Unsafe.TryGetPointer<SentryOverclockUpgrade>(owner, out var upgrade) == false)
            {
                sentry->FireRateMultiplier = FP._1;
                return;
            }

            sentry->FireRateMultiplier = upgrade->FireRateMultiplier > FP._0 ? upgrade->FireRateMultiplier : FP._1;
            sentry->RedlineThreshold = upgrade->RedlineThreshold;
            sentry->RedlineFireRateMultiplier = upgrade->RedlineFireRateMultiplier;
        }

        // The baseline Cannon, always armed, in slot 0 - the one weapon a sentry has with no Ascension
        // at all. Slot 0 is also what MK II later swaps, so keeping it a fixed, known slot matters.
        private void ApplyBaselineWeapon(Frame f, EntityRef owner, EntityRef sentry, FPVector3 sentryPosition, FPQuaternion chassisRotation)
        {
            if (BaselineWeapon.IsValid == false)
            {
                Log.Error("[Skill] SpawnSentrySkillAction has no BaselineWeapon assigned - the deployed sentry will have no Cannon at all");
                return;
            }

            SpawnBarrel(f, owner, sentry, sentryPosition, chassisRotation, BaselineWeapon, BaselineWeaponOffset, 0, default);
        }

        // Weapon Systems (see SentryWeaponSystemsSkillAction) - slots 1..3 only; slot 0 is the
        // baseline Cannon above and is never touched here.
        private void ApplyWeaponUpgrade(Frame f, EntityRef owner, EntityRef sentry, FPVector3 sentryPosition, FPQuaternion chassisRotation)
        {
            if (f.Unsafe.TryGetPointer<SentryWeaponUpgrade>(owner, out var upgrade) == false)
                return;

            for (int i = 1; i < 4; i++)
            {
                if (upgrade->WeaponData[i].IsValid == false)
                    continue;

                SpawnBarrel(f, owner, sentry, sentryPosition, chassisRotation, upgrade->WeaponData[i], upgrade->WeaponOffset[i], (byte)i, upgrade->Source[i]);
            }
        }

        private void SpawnBarrel(Frame f, EntityRef owner, EntityRef sentry, FPVector3 sentryPosition, FPQuaternion chassisRotation,
            AssetRef<WeaponDataAsset> weaponData, FPVector3 weaponOffset, byte slotIndex, AssetRef<SentryWeaponSystemsSkillAction> source)
        {
            if (BarrelPrototype.IsValid == false)
            {
                Log.Error("[Skill] SpawnSentrySkillAction has a weapon slot to arm but no BarrelPrototype assigned - nothing spawned");
                return;
            }

            EntityRef barrel = f.Create(BarrelPrototype);
            FPVector3 barrelPosition = sentryPosition;

            if (f.Unsafe.TryGetPointer<Transform3D>(barrel, out var barrelTransform) == true)
            {
                barrelPosition = sentryPosition + chassisRotation * weaponOffset;
                barrelTransform->Position = barrelPosition;
                barrelTransform->Rotation = chassisRotation;
            }

            // A barrel's position/rotation is entirely scripted by SentryBarrelSystem re-anchoring it
            // to the chassis every tick - it must never be a physically-simulated body of its own, or
            // Quantum's physics engine keeps integrating its own gravity/velocity on top of (or
            // instead of) that scripted anchor.
            if (f.Unsafe.TryGetPointer<PhysicsBody3D>(barrel, out var barrelBody) == true)
            {
                barrelBody->IsKinematic = true;
            }

            // WeaponData is set BEFORE Add (not AddOrGet-then-set) so WeaponSystem's own
            // ISignalOnComponentAdded<Weapon> fires with real data already in place and equips it
            // immediately, instead of firing once against a still-empty AssetRef.
            Weapon weapon = default;
            weapon.WeaponData = weaponData;
            f.Add(barrel, weapon);

            f.AddOrGet<SentryBarrel>(barrel, out var sentryBarrel);
            sentryBarrel->Sentry = sentry;
            sentryBarrel->WeaponOffset = weaponOffset;
            sentryBarrel->SlotIndex = slotIndex;
            sentryBarrel->Source = source;

            sentryBarrel->BaseFireCooldownMultiplier = FP._1;

            if (f.Unsafe.TryGetPointer<Weapon>(barrel, out var equipped) == true)
            {
                // The barrel's own un-modified fire cooldown, captured right after equip - every
                // sentry-wide fire-rate effect composes against THIS rather than against whatever the
                // multiplier happens to be this tick, which is what stops repeated per-tick
                // application from compounding. See SentryBarrelSystem.
                sentryBarrel->BaseFireCooldownMultiplier = equipped->FireCooldownMultiplier;

                // Lux's Skill Damage, baked in at deploy. A barrel fires its Weapon with ITSELF as the
                // owner, and it has no CharacterStats - so DamageUtility.ResolveOutgoingDamage returns
                // at its own stats gate and a sentry shot would otherwise receive none of her build.
                //
                // Compounded into Weapon.DamageMultiplier rather than written as an absolute: that
                // field is seeded to 1 at Equip and is exactly where every other standing weapon
                // modifier already accumulates (DamageMultiplierWeaponPerkData.Apply,
                // WeaponSystem.AddLevel), and WeaponSystem re-reads WeaponDataAsset.Damage against it
                // fresh on every shot, so Inspector tuning still takes effect live.
                //
                // Resolved ONCE at deploy, deliberately - the same "baked at spawn" contract the
                // sentry's Range, its Overload Core blast and its Fortification copies all follow, so
                // a damage upgrade taken mid-run applies to the NEXT sentry rather than retroactively
                // buffing machines already in the field.
                equipped->DamageMultiplier *= StatUtility.GetSkillDamageMultiplier(f, owner);
            }

            // Purely cosmetic - lets the chassis's own view (SentryView) activate the matching gun
            // sprite out of its authored list; the sim never re-reads this.
            f.Events.SentryBarrelSpawned(sentry, barrel, slotIndex);
        }

        // Fortification ranks 2-3 - copied onto the spawned sentry itself (not re-read off Lux later)
        // so SentryAuraSystem keeps supporting allies even if she's no longer nearby or alive.
        private static void ApplyFortificationUpgrade(Frame f, EntityRef owner, EntityRef spawned)
        {
            if (f.Unsafe.TryGetPointer<SentryFortificationUpgrade>(owner, out var upgrade) == false)
                return;

            f.AddOrGet<SentryFortificationUpgrade>(spawned, out var copy);
            *copy = *upgrade;

            // Covering Fire's "one denial per hero per TURRET" allowance. Lives on the sentry rather
            // than on Lux for exactly that reason - a fresh deploy is a fresh entity and therefore a
            // fresh allowance for everyone, two of her sentries each track their own, and two Luxes
            // never share one. Same AreaAllyBudget primitive Zara's Totem/Speaker healing caps use.
            //
            // Only added when the rank is actually held: MaxGuardsPerAlly 0 (or no component at all)
            // denies outright, so a rank-1 sentry grants nothing.
            if (upgrade->GuardDuration <= FP._0)
                return;

            f.AddOrGet<AreaAllyBudget>(spawned, out var budget);
            budget->MaxGuardsPerAlly = upgrade->GuardsPerAlly;
        }

        // Overload Core - same copy-onto-spawned reasoning. Damage is resolved from a percentage of
        // Sentry Skill Damage into a flat number HERE, at deploy time, so a sentry's blast reflects
        // Lux's skill damage as it was when she built the machine.
        private static void ApplyOverloadUpgrade(Frame f, EntityRef owner, EntityRef spawned)
        {
            if (f.Unsafe.TryGetPointer<SentryOverloadUpgrade>(owner, out var upgrade) == false)
                return;

            f.AddOrGet<SentryOverloadUpgrade>(spawned, out var copy);
            *copy = *upgrade;
        }

        // Field Modifications - the live stack state lives on the SENTRY (see SentryModifications), so
        // stacks belong to one machine and die with it. Only added when Lux actually holds the
        // Ascension (MaxStacks > 0).
        private static void ApplyFieldModifications(Frame f, EntityRef owner, EntityRef spawned)
        {
            if (f.Unsafe.TryGetPointer<LuxScrapCollector>(owner, out var collector) == false || collector->FieldModMaxStacks == 0)
                return;

            f.AddOrGet<SentryModifications>(spawned, out var modifications);
            modifications->Stacks = 0;
            modifications->MaxStacks = collector->FieldModMaxStacks;
            modifications->DamagePerStack = collector->FieldModDamagePerStack;
            modifications->FireRatePerStack = collector->FieldModFireRatePerStack;
            modifications->MkIIWeapon = collector->MkIIWeapon;
            modifications->MkIIApplied = false;
        }
    }
}
