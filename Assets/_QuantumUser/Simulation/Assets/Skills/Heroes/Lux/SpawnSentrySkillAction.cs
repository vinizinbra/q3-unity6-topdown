namespace Quantum
{
    using Photon.Deterministic;

    // Dedicated spawn action (not the generic SpawnEntitySkillAction) because it needs to configure
    // the spawned sentry's Sentry/Weapon/Shield/aura fields in the same method call that has the
    // entity reference - SkillSlot has nowhere safe to stash "the entity I just spawned" for a
    // second action to pick up later (an EntityRef field on SkillSlot silently breaks the Upgrades
    // array's own Inspector authoring - CharacterSkills.qtn's own SkillSlot.ProjectilePending comment
    // documents exactly this, after a previous EntityRef field there caused it). Mirrors
    // SpawnVortexEffectData's own shape: spawn, then read each Begin-only upgrade off the caster and
    // bake a copy onto the spawned entity, so the sentry keeps working even once the caster's own
    // activation has long since ended.
    //
    // Priority deliberately high (opposite of IncreaseAreaSkillAction's -100) - this lives in the
    // skill's baseline Actions, while every SentryXxxUpgrade grant (Increase Range, Add Shield, Add
    // Fire Rate, ...) lives in slot->Upgrades, and SkillSystem.InvokeActions runs baseline Actions
    // before Upgrades whenever Priority ties (both default to 0). Left at the default, this action
    // would read each upgrade's component BEFORE that same activation's own grant actions (re)write
    // it, so a sentry deployed the very first time an upgrade is picked would spawn without it - only
    // catching up starting the NEXT cast, once a prior activation's grant had already run once. A
    // high Priority forces this to run last within the phase regardless of Actions/Upgrades list
    // position, so it always reads this activation's freshly (re)granted values.
    public unsafe partial class SpawnSentrySkillAction : SkillActionData
    {
        public AssetRef<EntityPrototype> Prototype;

        // Transform3D + InputSource + SentryBarrel - Weapon is added dynamically per equipped slot
        // (see ApplyWeaponUpgrade), not pre-authored, since a sentry might arm anywhere from 0 to 4
        // of them.
        public AssetRef<EntityPrototype> BarrelPrototype;

        // No longer a hard despawn timer - see ResolveDecayRate, which turns this into how long a
        // fully-undamaged sentry survives its own Health drain instead.
        public FP Duration = 20;
        public FP Range = 8;

        // Local-space (X=right, Y=up, Z=forward), rotated by the caster's own aim yaw - same
        // convention SpawnEntitySkillAction's own Offset uses.
        public FPVector3 Offset = new FPVector3(0, 0, 2);

        public SpawnSentrySkillAction()
        {
            Phase = SkillActionPhase.Begin;
            Priority = 100;
        }

        // {0} = Range, {1} = Duration - both flat values (not percents) - e.g. "Deploys a sentry
        // turret at Lux's position with {0} range that decays over about {1} seconds if left
        // undamaged, applying any equipped weapon, shield, and aura upgrades to it."
        protected override object[] DescriptionArgs => new object[] { Range, Duration };

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Aim only decides WHERE this spawns (in front of Lux's current facing) - not how it's
            // oriented. The chassis keeps whatever rotation Prototype was authored with instead of
            // snapping to her aim, so it doesn't visually spin to match wherever she happened to be
            // looking when she cast. Barrel offsets below are resolved against that same
            // prototype-authored rotation (not Aim either), so muzzle placement stays visually
            // consistent with however the chassis is actually oriented.
            FPQuaternion aimFacing = FPQuaternion.Euler(0, filter.Aim->Angle, 0);
            FPVector3 position = filter.Transform3D->Position + aimFacing * Offset;

            EntityRef spawned = SpawnedEntitySpawner.Spawn(f, filter.Entity, Prototype, Duration, position, DamageSource.Skill);

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

            sentry->Range = Range + ResolveRangeBonus(f, filter.Entity);
            sentry->Owner = filter.Entity;
            sentry->DecayRate = ResolveDecayRate(f, spawned);

            ApplyWeaponUpgrade(f, filter.Entity, spawned, position, chassisRotation);
            ApplyShieldUpgrade(f, filter.Entity, spawned);
            ApplyFireRateAuraUpgrade(f, filter.Entity, spawned);
            ApplyShieldAreaRateAuraUpgrade(f, filter.Entity, spawned);
            ApplyOverloadUpgrade(f, filter.Entity, spawned);

            Log.Debug($"[Skill] {filter.Entity} deployed a sentry {spawned} at {position}, range {sentry->Range}");
        }

        // SentryRangeUpgrade (see SentryIncreaseRangeSkillAction) - additive on top of this action's
        // own authored Range, same "bonus stacks on authored value" shape SpawnRadiusUpgrade/
        // IncreaseDurationUpgrade already use elsewhere.
        private static FP ResolveRangeBonus(Frame f, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<SentryRangeUpgrade>(owner, out var upgrade) == false)
                return FP._0;

            return upgrade->RangeBonus;
        }

        // Turns Duration into Sentry.DecayRate (MaxHealth / Duration) instead of a plain
        // DestroyAfterTime countdown - see SentryDecaySystem and Sentry.qtn's own DecayRate comment.
        // SpawnedEntitySpawner.Spawn already added DestroyAfterTime with the fully-resolved duration
        // (including any IncreaseDurationUpgrade stretch, via its own ResolveDuration) - read that
        // value once here for the decay math, then remove the component so the generic timer doesn't
        // ALSO destroy this entity independently of Health.
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

        // SentryWeaponUpgrade (see SentryAddWeaponSkillAction) - without this, the sentry has no
        // barrels/weapons at all. One SentryBarrel child entity per equipped slot (a valid
        // WeaponData), not 4 fields on the sentry itself - a single entity can only ever carry one
        // Weapon component, so simultaneous multi-weapon fire needs one entity per gun. Each
        // barrel's own Transform3D.Position is the muzzle offset already resolved into world space
        // and baked in once - WeaponSystem then just reads its caster position as normal, no runtime
        // hold-offset resolution needed for a Sentry at all.
        private void ApplyWeaponUpgrade(Frame f, EntityRef owner, EntityRef sentry, FPVector3 sentryPosition, FPQuaternion chassisRotation)
        {
            if (f.Unsafe.TryGetPointer<SentryWeaponUpgrade>(owner, out var upgrade) == false)
                return;

            for (int i = 0; i < 4; i++)
            {
                if (upgrade->WeaponData[i].IsValid == false)
                    continue;

                SpawnBarrel(f, owner, sentry, sentryPosition, chassisRotation, upgrade->WeaponData[i], upgrade->WeaponOffset[i], (byte) i, upgrade->Source[i]);
            }
        }

        private void SpawnBarrel(Frame f, EntityRef owner, EntityRef sentry, FPVector3 sentryPosition, FPQuaternion chassisRotation,
            AssetRef<WeaponDataAsset> weaponData, FPVector3 weaponOffset, byte slotIndex, AssetRef<SentryAddWeaponSkillAction> source)
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
            // instead of) that scripted anchor, letting it drift away under its own free fall
            // uncorrected instead of following the chassis. Forced here rather than trusted to however
            // BarrelPrototype happened to author PhysicsBody3D.
            if (f.Unsafe.TryGetPointer<PhysicsBody3D>(barrel, out var barrelBody) == true)
            {
                barrelBody->IsKinematic = true;
            }

            Log.Debug($"[Skill] slot {slotIndex} barrel {barrel} spawned at {barrelPosition} (sentry {sentry} at {sentryPosition}, offset {weaponOffset})");

            // WeaponData is set BEFORE Add (not AddOrGet-then-set) so WeaponSystem's own
            // ISignalOnComponentAdded<Weapon> fires with real data already in place and equips it
            // immediately, instead of firing once against a still-empty AssetRef.
            Weapon weapon = default;
            weapon.WeaponData = weaponData;
            f.Add(barrel, weapon);

            ApplyFireRateUpgrade(f, owner, barrel);

            f.AddOrGet<SentryBarrel>(barrel, out var sentryBarrel);
            sentryBarrel->Sentry = sentry;
            sentryBarrel->WeaponOffset = weaponOffset;
            sentryBarrel->SlotIndex = slotIndex;
            sentryBarrel->Source = source;

            // Purely cosmetic - lets the chassis's own view (SentryView) activate the matching gun
            // sprite out of its authored list and resolve Barrel's own View/Transform; the sim never
            // re-reads this.
            f.Events.SentryBarrelSpawned(sentry, barrel, slotIndex);
        }

        // SentryFireRateUpgrade (see SentryIncreaseFireRateSkillAction) - permanently compounds into
        // this barrel's own Weapon.FireCooldownMultiplier, same math FireRateWeaponPerkData.Apply
        // already uses for player weapon perks (see Weapon.qtn for why this is a multiplier, not a
        // baked absolute). Deliberately NOT the temporary Haste status effect
        // SentryFireRateAuraUpgrade/SentryAuraSystem grants to ALLIES - a barrel has neither
        // CharacterStats nor StatusEffects, and this is a permanent stat on the sentry's own guns.
        private static void ApplyFireRateUpgrade(Frame f, EntityRef owner, EntityRef barrel)
        {
            if (f.Unsafe.TryGetPointer<SentryFireRateUpgrade>(owner, out var upgrade) == false)
                return;

            if (f.Unsafe.TryGetPointer<Weapon>(barrel, out var weapon) == false)
                return;

            weapon->FireCooldownMultiplier = FPMath.Max(FP._0, weapon->FireCooldownMultiplier / upgrade->AttackSpeedMultiplier);
        }

        // SentryShieldUpgrade (see SentryAddShieldSkillAction) - a vanilla sentry has no Shield at
        // all, same optional-component pattern every other shielded entity in the game follows.
        private static void ApplyShieldUpgrade(Frame f, EntityRef owner, EntityRef spawned)
        {
            if (f.Unsafe.TryGetPointer<SentryShieldUpgrade>(owner, out var upgrade) == false)
                return;

            f.AddOrGet<Shield>(spawned, out var shield);
            shield->Max = upgrade->Max;
            shield->Current = upgrade->Max;
            shield->RechargeDelay = upgrade->RechargeDelay;
            shield->RechargeRate = upgrade->RechargeRate;
        }

        // SentryFireRateAuraUpgrade (see SentryAddFireRateSkillAction) - copied onto the spawned
        // sentry itself (not re-read off the caster later) so SentryAuraSystem keeps buffing allies
        // even if the caster who deployed this sentry is no longer around.
        private static void ApplyFireRateAuraUpgrade(Frame f, EntityRef owner, EntityRef spawned)
        {
            if (f.Unsafe.TryGetPointer<SentryFireRateAuraUpgrade>(owner, out var upgrade) == false)
                return;

            f.AddOrGet<SentryFireRateAuraUpgrade>(spawned, out var copy);
            copy->AttackSpeedMultiplier = upgrade->AttackSpeedMultiplier;
        }

        // SentryShieldAreaRateUpgrade (see SentryAddShieldAreaRateSkillAction) - same
        // copy-onto-spawned reasoning as ApplyFireRateAuraUpgrade.
        private static void ApplyShieldAreaRateAuraUpgrade(Frame f, EntityRef owner, EntityRef spawned)
        {
            if (f.Unsafe.TryGetPointer<SentryShieldAreaRateUpgrade>(owner, out var upgrade) == false)
                return;

            f.AddOrGet<SentryShieldAreaRateUpgrade>(spawned, out var copy);
            copy->ShieldRegenMultiplier = upgrade->ShieldRegenMultiplier;
        }

        // SentryOverloadUpgrade (see SentryAddOverloadSkillAction) - copied onto the spawned sentry
        // itself, same copy-onto-spawned reasoning as ApplyFireRateAuraUpgrade, so
        // DamageUtility.TrySentryOverload keeps working even once the caster who deployed this sentry
        // is no longer around. Fully independent from the enemy kill-chain ExplodeOnDeath mechanic -
        // its own Radius/Damage, nothing shared.
        private static void ApplyOverloadUpgrade(Frame f, EntityRef owner, EntityRef spawned)
        {
            if (f.Unsafe.TryGetPointer<SentryOverloadUpgrade>(owner, out var upgrade) == false)
                return;

            f.AddOrGet<SentryOverloadUpgrade>(spawned, out var copy);
            copy->Damage = upgrade->Damage;
            copy->Radius = upgrade->Radius;
            copy->Source = upgrade->Source;
        }
    }
}
