namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;
    using UnityEngine.Scripting;

    [Preserve]
    public unsafe class WeaponSystem : SystemMainThreadFilter<WeaponSystem.Filter>, ISignalOnComponentAdded<Weapon>
    {
        // Covers a weapon authored straight onto a prototype (e.g. a Sentry barrel's WeaponData,
        // baked in ApplyWeaponUpgrade); a rolled drop calls Equip itself (see WeaponGenerator) once
        // it has filled in Perks. A hero's own prototype now leaves WeaponData empty on purpose -
        // CharacterSystem.SeedWeapon equips CharacterData.StartingWeapon once the rest of the
        // prototype has materialized, so this silently skips rather than logging Equip's "no
        // WeaponDataAsset" error for every hero spawn.
        public void OnAdded(Frame f, EntityRef entity, Weapon* weapon)
        {
            if (weapon->WeaponData.IsValid == false)
                return;

            Equip(f, entity, weapon, weapon->WeaponData);
        }

        // Reads perks from weapon->Perks, so fill that in first. Re-seeds every stat from the asset
        // before re-applying, which is what lets this be called again on a weapon swap without the
        // previous roll's perks staying baked into the new weapon. owner is the entity holding this
        // Weapon - needed (only) to re-bake owner-level, non-perk passive effects that must survive
        // a weapon swap, see ApplyPixieExplosiveWeapon below.
        public static void Equip(Frame f, EntityRef owner, Weapon* weapon, AssetRef<WeaponDataAsset> weaponDataRef)
        {
            if (weaponDataRef.IsValid == false)
            {
                Log.Error("[Weapon] Equip called with no WeaponDataAsset - stats stay at zero");
                return;
            }

            weapon->WeaponData = weaponDataRef;

            SeedStats(f, owner, weapon);
            ApplyPerks(f, owner, weapon);
            ApplyPixieExplosiveWeapon(f, owner, weapon);

            weapon->Ammo = weapon->MagazineSize;
            weapon->FireCooldownTimer = FP._0;
            weapon->ReloadTimer = FP._0;
            weapon->TimeSinceFireReleased = FP._0;

            // Generic View hook - see Events.qtn's own comment on why this fires unconditionally
            // for every equip path rather than each caller raising its own event.
            f.Events.WeaponEquipped(owner, weaponDataRef);
        }

        // Called by StoreUtility.BuyWeaponLevelUp when a player buys the Store's guaranteed
        // "Increase Weapon Level" offer (see docs/store-blacksmith.md) - compounds
        // damageBonusPerLevel into DamageMultiplier, the exact same idiom
        // DamageMultiplierWeaponPerkData.Apply already uses for a rolled perk, so it composes with
        // every other damage-multiplier source identically. Level itself is bookkeeping only
        // (pricing/display - see Weapon.qtn's own comment), never read back into damage math.
        public static void AddLevel(Weapon* weapon, FP damageBonusPerLevel)
        {
            weapon->Level++;
            weapon->DamageMultiplier = CompoundLevelMultiplier(weapon->DamageMultiplier, damageBonusPerLevel);
        }

        // Resolves what DamageMultiplier would compound to after `levels` calls to AddLevel starting
        // from a fresh (Multiplier = 1) weapon - same per-level compounding step AddLevel itself
        // applies in place, extracted here so a not-yet-granted weapon (e.g. a Store offer card's own
        // damage preview, see GameplayUiController.BuildStoreWeaponCardData) can show the correct
        // level-adjusted damage without mutating a real Weapon component.
        public static FP ResolveLevelDamageMultiplier(int levels, FP damageBonusPerLevel)
        {
            FP multiplier = FP._1;

            for (int i = 0; i < levels; i++)
                multiplier = CompoundLevelMultiplier(multiplier, damageBonusPerLevel);

            return multiplier;
        }

        private static FP CompoundLevelMultiplier(FP current, FP damageBonusPerLevel)
        {
            return FPMath.Max(FP._0, current * (FP._1 + damageBonusPerLevel));
        }

        // Pixie's Explosive Rounds passive ascension (see ExplosiveRoundsPassiveUpgradeData/
        // PixieExplosiveWeapon.qtn) - baked here, after perks, so it survives every weapon swap
        // instead of only the weapon she was holding when she picked it. Forces every shot to proc
        // (Interval=1) rather than only every Nth - strictly more frequent than any real Explosive
        // Sequence perk roll could ever be, so this always wins outright; Radius/DamageMultiplier
        // still compose with FPMath.Max exactly like ExplosiveSequenceWeaponPerkData.Apply does,
        // rather than overriding an actual perk roll. No-op for every hero without the component.
        // Also called directly by ExplosiveRoundsPassiveUpgradeData.Apply so picking it mid-run
        // takes effect on the weapon she's already holding, not just her next equip.
        public static void ApplyPixieExplosiveWeapon(Frame f, EntityRef owner, Weapon* weapon)
        {
            if (f.Unsafe.TryGetPointer<PixieExplosiveWeapon>(owner, out var explosive) == false)
                return;

            f.AddOrGet<WeaponPostImpactProcs>(owner, out var procs);
            procs->ExplosiveSequenceInterval = 1;
            procs->ExplosiveSequenceRadius = FPMath.Max(procs->ExplosiveSequenceRadius, explosive->Radius);
            procs->ExplosiveSequenceDamageMultiplier = FPMath.Max(procs->ExplosiveSequenceDamageMultiplier, explosive->DamageMultiplier);

            // Interval = 1 makes every shot ELIGIBLE; the actual frequency is this chance (see
            // PixieExplosiveWeapon.ProcChance). Reworked from the old always-on version, which made
            // literally every weapon hit explode - far too frequent, and the first thing the balance
            // brief calls out for this line. ProcCooldown is 0 unless a playtest turns it on.
            procs->ExplosiveSequenceChance = explosive->ProcChance;
            procs->ExplosiveSequenceCooldown = explosive->ProcCooldown;
        }

        // Optional per-weapon internal cooldown between explosive procs - only ticks when something
        // actually authored one (see WeaponPostImpactProcs.ExplosiveSequenceCooldown), so this is a
        // failed pointer lookup and nothing else for every weapon that didn't.
        private static void TickExplosiveProcCooldown(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<WeaponPostImpactProcs>(entity, out var procs) == false
                || procs->ExplosiveSequenceCooldownRemaining <= FP._0)
                return;

            procs->ExplosiveSequenceCooldownRemaining -= f.DeltaTime;
        }

        // Every perk-mutable stat starts life as its authored value; perks then edit these in place.
        // Damage/FireCooldown are the exception - see Weapon.qtn - so their multipliers reset to 1
        // here instead of copying an asset value.
        private static void SeedStats(Frame f, EntityRef owner, Weapon* weapon)
        {
            WeaponDataAsset weaponData = f.FindAsset(weapon->WeaponData);

            weapon->DamageMultiplier = FP._1;
            weapon->FireCooldownMultiplier = FP._1;
            weapon->RangeMultiplier = FP._1;
            weapon->Level = 0;
            weapon->MagazineSize = weaponData.MagazineSize;
            weapon->ReloadDuration = weaponData.ReloadDuration;
            weapon->CriticalChance = weaponData.CriticalChance;
            weapon->CriticalDamageBonus = weaponData.CriticalDamageBonus;

            SeedPerkRoster(f, owner);
        }

        // None of these have an authored value on WeaponDataAsset to seed from - they only ever
        // come from a perk's own Apply - so every optional perk component is unconditionally
        // removed here before ApplyPerks re-adds only what the new roll actually grants, extending
        // the same "SeedStats resets everything before ApplyPerks re-bakes it" invariant Equip's own
        // doc comment already promises to the wider perk roster (see docs/weapon-perks.md), so a
        // weapon swap can't carry over a previous roll's perk state. Emergency Reload's
        // CharacterStats bonus must be unwound with the OLD weapon's WeaponReloadHooks values before
        // that component disappears - RevertEmergencyReload already self-guards on
        // EmergencyReloadApplied, so it's always safe to call unconditionally first, same
        // revert-then-remove idiom BerserkSkillData.End()/RageOverdriveUtility.Revert use.
        private static void SeedPerkRoster(Frame f, EntityRef owner)
        {
            RevertEmergencyReload(f, owner);

            f.Remove<WeaponMagazinePositionPerks>(owner);
            f.Remove<WeaponRampState>(owner);
            f.Remove<WeaponEchoState>(owner);
            f.Remove<WeaponFireTimeMods>(owner);
            f.Remove<WeaponPostImpactProcs>(owner);
            f.Remove<WeaponReloadHooks>(owner);
            f.Remove<WeaponOnKillReactions>(owner);
            f.Remove<WeaponOnCritReactions>(owner);
            f.Remove<WeaponHitTrackingPerks>(owner);
            f.Remove<WeaponElementInfusion>(owner);
        }

        private static void ApplyPerks(Frame f, EntityRef owner, Weapon* weapon)
        {
            var perks = weapon->Perks;

            for (int i = 0; i < perks.Length; ++i)
            {
                if (perks[i].IsValid == false)
                    continue;

                f.FindAsset(perks[i]).Apply(f, owner, weapon);
            }

            Log.Debug($"[Weapon] equipped - damageMultiplier={weapon->DamageMultiplier}, " +
                $"cooldownMultiplier={weapon->FireCooldownMultiplier}, magazine={weapon->MagazineSize}, crit={weapon->CriticalChance}");
        }

        // Grants a perk after the fact (a level-up, not a drop roll) - records it so UI can read the
        // roll back, then bakes it. False when every slot is already taken.
        public static bool AddPerk(Frame f, EntityRef owner, Weapon* weapon, AssetRef<WeaponPerkData> perkRef)
        {
            if (perkRef.IsValid == false)
                return false;

            var perks = weapon->Perks;

            for (int i = 0; i < perks.Length; ++i)
            {
                if (perks[i].IsValid)
                    continue;

                perks[i] = perkRef;
                f.FindAsset(perkRef).Apply(f, owner, weapon);

                return true;
            }

            Log.Error($"[Weapon] all {perks.Length} perk slots taken - {perkRef} not granted");

            return false;
        }

        // GetPlayerCommand only returns non-null on the tick a sent command actually lands - unlike
        // polled Input, this fires exactly once per SendCommand call, not every tick. PlayerLink
        // isn't part of Filter (a non-player shooter like Lux's sentry gun has no PlayerLink and
        // therefore nothing sending it commands - see HasFireDriver), so it's looked up directly
        // here instead. See GrantWeaponPerkCommand for why this has to be a command rather than a
        // direct call from the View (WeaponPerkDebugTrigger today; a level-up/pickup screen
        // eventually).
        private static void ProcessGrantPerkCommand(Frame f, ref Filter filter)
        {
            if (f.Unsafe.TryGetPointer<PlayerLink>(filter.Entity, out var playerLink) == false)
                return;

            if (f.GetPlayerCommand(playerLink->Player) is not GrantWeaponPerkCommand command)
                return;

            if (AddPerk(f, filter.Entity, filter.Weapon, command.Perk) == true)
            {
                Log.Debug($"[Weapon] {filter.Entity} was granted {command.Perk} via command");
            }
        }

        public override void Update(Frame f, ref Filter filter)
        {
            if (filter.Weapon->FireCooldownTimer > FP._0)
            {
                filter.Weapon->FireCooldownTimer -= f.DeltaTime;
            }

            // Processed unconditionally, ahead of the stun/input early-outs below - a granted perk
            // shouldn't wait on the recipient being able to act this tick. Same reasoning for the
            // ramp decay/Killer Instinct timer/pending echoes below - none of them should freeze
            // just because the wielder is stunned or holding no input this tick.
            ProcessGrantPerkCommand(f, ref filter);
            TickRamp(f, filter.Entity, filter.Weapon);
            TickKillerInstinct(f, filter.Entity, f.DeltaTime);
            TickPendingEchoes(f, filter.Entity, filter.Weapon, f.DeltaTime);
            TickPendingDoubleTap(f, filter.Entity, filter.Weapon, f.DeltaTime);
            TickExplosiveProcCooldown(f, filter.Entity);

            if (StatusEffectUtility.IsStunned(f, filter.Entity) == true)
                return;

            // Downed/KO can't fire either (see docs/revive.md) - checked separately from the POI
            // lock below since it has its own gate (PlayerLifeStateUtility.IsIncapacitated), not a
            // session component PoiInteractionLockUtility would otherwise need to know about.
            if (PlayerLifeStateUtility.IsIncapacitated(f, filter.Entity) == true)
                return;

            // Same per-player input lock SkillSystem also gates on - a Cursed Rift/Store/Blacksmith
            // Choice Window (or an active Revive channel) open for this player blocks firing
            // without touching GameplaySystemGroup/Time.timeScale (see docs/breathing-poi.md/
            // docs/revive.md).
            if (PoiInteractionLockUtility.IsInputLocked(f, filter.Entity) == true)
                return;

            if (HasFireDriver(f, filter.Entity) == false)
                return;

            bool canFire;

            if (f.RuntimeConfig.DebugManualFireInput == true
                && f.Unsafe.TryGetPointer<PlayerLink>(filter.Entity, out var playerLink) == true)
            {
                // Debug override: replaces auto-attack entirely for a real player - firing follows
                // the Fire input only, target locked or not, instead of Aim.Target.
                // FireShot/ResolveAimDirection already fall back to facing direction when target is
                // None, so firing untargeted is safe as-is. Non-player shooters (Lux's sentry gun)
                // have no PlayerLink and stay purely target-driven regardless of this flag.
                canFire = f.GetPlayerInput(playerLink->Player)->Fire.IsDown == true;
            }
            else
            {
                // Auto-attack: firing is gated on Aim.Target (already re-resolved every tick by
                // AimSystem/SentryBarrelSystem) rather than a held Fire input - whoever's holding
                // this weapon shoots on its own the moment a target is in range, no button needed.
                canFire = filter.Aim->Target != EntityRef.None;
            }

            if (UpdateReload(f, filter.Entity, filter.Weapon, canFire))
                return;

            if (canFire == false || filter.Weapon->FireCooldownTimer > FP._0)
                return;

            WeaponDataAsset weaponData = f.FindAsset(filter.Weapon->WeaponData);

            FPVector3 casterPosition = filter.Transform3D->Position;
            FP aimAngle = filter.Aim->Angle;
            FPVector3 flatDirection = FPQuaternion.Euler(0, aimAngle, 0) * FPVector3.Forward;
            bool aimAtCenter = ResolveAimsAtCenter(f, weaponData);
            FPVector3 holdOffset = StatUtility.GetWeaponHoldOffset(f, filter.Entity, filter.Aim->FacingSign);
            FPVector3 spawnPosition = ProjectileSpawner.ResolveSpawnOrigin(casterPosition, casterPosition, aimAngle, weaponData.SpawnAnchor, weaponData.SpawnOffset) + holdOffset;

            // From the real spawn point, not the caster's own position - they can differ (see
            // SpawnOffset), and a lob needs the correct elevation to compute a believable arc.
            FPVector3 aimDirection = ProjectileAimUtility.ResolveAimDirection(f, filter.Aim->Target, spawnPosition, flatDirection, aimAtCenter);

            // Magazine-position perks (Opening Burst/Execution Rounds/Final Round/Escalating
            // Rounds) all read off Ammo/MagazineSize - see ResolveMagazineFraction - rather than
            // tracking their own shot index. Read before Ammo decrements below, since "the last
            // bullet" and "shots fired so far" both mean relative to the shot about to leave now.
            FP magazineFraction = ResolveMagazineFraction(filter.Weapon);
            bool isLastBullet = filter.Weapon->Ammo == 1;
            bool isEchoEligibleShot = filter.Weapon->MagazineSize - filter.Weapon->Ammo + 1 <= 3;

            f.Unsafe.TryGetPointer<WeaponPostImpactProcs>(filter.Entity, out var postImpactProcs);
            bool isExplosiveProc = ResolveExplosiveProc(f, postImpactProcs);
            bool isCataclysm = postImpactProcs != null && postImpactProcs->HasCataclysmRound == true && isLastBullet == true;

            // Read fresh off the asset every fire, not a baked stat - see Weapon.qtn - so tuning
            // WeaponDataAsset.Damage/FireRate in the Inspector applies immediately to
            // already-equipped weapons instead of only the next Equip.
            FP damage = ResolveLiveDamage(f, filter.Entity, weaponData.Damage * filter.Weapon->DamageMultiplier, magazineFraction, isLastBullet);

            // Kai's Phantom Strike - consumed here, once, on the actual next shot fired (hit or miss),
            // not on hit landing - "next weapon hit" reads naturally as "next shot", and this keeps the
            // damage bonus and the bonus pierce below consuming the same charge at the same single
            // resolution point. Applies to both this shot and a Double Tap proc off it (same damage
            // local reused for both FireShot calls below), same as every other multiplier baked in
            // above this point. A flat amount (not a bool) so rank 2/3's higher PierceBonus survives.
            int grantPierceAmount = 0;

            if (f.Has<PhantomStrikeCharge>(filter.Entity) == true
                && f.Unsafe.TryGetPointer<PhantomStrikeUpgrade>(filter.Entity, out var phantomStrike) == true)
            {
                damage *= FP._1 + phantomStrike->DamageMultiplierBonus;
                grantPierceAmount = phantomStrike->PierceBonus;
                f.Remove<PhantomStrikeCharge>(filter.Entity);
            }

            FireShot(f, filter.Entity, filter.Weapon, weaponData, damage, casterPosition, aimAngle, holdOffset,
                spawnPosition, aimDirection, filter.Aim->Target, aimAtCenter, isExplosiveProc, isCataclysm, grantPierceAmount);

            if (f.Unsafe.TryGetPointer<WeaponFireTimeMods>(filter.Entity, out var fireMods) == true
                && fireMods->DoubleTapChance > FP._0 && DamageUtility.RollChance(f, fireMods->DoubleTapChance) == true)
            {
                if (fireMods->DoubleTapDelay > FP._0)
                {
                    // Silently dropped if one's already pending (a rapid-fire weapon outrunning
                    // DoubleTapDelay) rather than overwriting it - same "don't stall/replace the
                    // older one" precedent EnqueueEcho already uses for its own queue.
                    if (fireMods->PendingDoubleTap.Delay <= FP._0)
                    {
                        fireMods->PendingDoubleTap = new PendingDoubleTapShot
                        {
                            Delay = fireMods->DoubleTapDelay,
                            SpawnPosition = spawnPosition,
                            AimDirection = aimDirection,
                            Damage = damage,
                            IsExplosiveProc = isExplosiveProc,
                            IsCataclysm = isCataclysm,
                            GrantPierceAmount = grantPierceAmount
                        };
                    }
                }
                else
                {
                    FireShot(f, filter.Entity, filter.Weapon, weaponData, damage, casterPosition, aimAngle, holdOffset,
                        spawnPosition, aimDirection, filter.Aim->Target, aimAtCenter, isExplosiveProc, isCataclysm, grantPierceAmount);
                }
            }

            if (f.Unsafe.TryGetPointer<WeaponEchoState>(filter.Entity, out var echoState) == true
                && (echoState->HasInfiniteEcho == true || (echoState->HasEchoChamber == true && isEchoEligibleShot == true)))
            {
                EnqueueEcho(echoState, spawnPosition, aimDirection, damage);
            }

            filter.Weapon->FireCooldownTimer = ResolveLiveFireCooldown(f, filter.Entity, filter.Weapon, weaponData, magazineFraction);

            // Run & Gun rank 3 (Max Dash Ascension) - a timed window where firing doesn't consume
            // Ammo at all, rather than a bigger magazine/instant reload.
            if (StatusEffectUtility.HasNoAmmoConsumption(f, filter.Entity) == false)
            {
                filter.Weapon->Ammo--;
            }

            filter.Weapon->TimeSinceFireReleased = FP._0;
            AimSystem.NotifyFired(filter.Aim);
            f.Events.PlayerFired(filter.Entity);

            if (filter.Weapon->Ammo <= 0)
                StartReload(f, filter.Entity, filter.Weapon);

            Log.Debug($"[Weapon] {filter.Entity} fired {weaponData.FireType} from {spawnPosition}, " +
                $"ammo={filter.Weapon->Ammo}/{filter.Weapon->MagazineSize}");
        }

        // Shared by the primary shot and Double Tap's free extra shot - both fire identically
        // (same resolved damage/isExplosiveProc/isCataclysm), Double Tap just doesn't re-consume
        // ammo/cooldown or re-roll its own proc chance.
        private static void FireShot(Frame f, EntityRef owner, Weapon* weapon, WeaponDataAsset weaponData, FP damage,
            FPVector3 casterPosition, FP aimAngle, FPVector3 holdOffset, FPVector3 spawnPosition, FPVector3 aimDirection,
            EntityRef target, bool aimAtCenter, bool isExplosiveProc, bool isCataclysm, int grantPierceAmount = 0)
        {
            switch (weaponData.FireType)
            {
                case WeaponFireType.Hitscan:
                    // grantPierceAmount (Kai's Phantom Strike) counts here too now that a hitscan beam
                    // pierces - see FireHitscanPellet. It used to be dropped on the floor for the same
                    // reason Piercing Rounds/Ricochet were: both perks only ever wrote Projectile
                    // fields, so a raycast that hit once and stopped could never express either.
                    FireHitscan(f, owner, weapon, weaponData, damage, spawnPosition, aimDirection, isExplosiveProc, isCataclysm, grantPierceAmount);
                    break;

                case WeaponFireType.Projectile:
                    FireProjectile(f, owner, weapon, weaponData, damage, casterPosition, aimAngle, holdOffset, spawnPosition, aimDirection,
                        target, aimAtCenter, isExplosiveProc, isCataclysm, grantPierceAmount);
                    break;
            }
        }

        // Decays the shared ramp pool (Relentless Fire/Suppressive Cycle/Overcharge Cycle) back to
        // 0 once the wielder has held fire released past RampDecayGrace - a snap reset rather than
        // a gradual per-tick drain, same "read live every shot, nothing to revert" idiom the pool
        // already uses for its bonus math (see ResolveLiveDamage/ResolveLiveFireCooldown).
        private static void TickRamp(Frame f, EntityRef owner, Weapon* weapon)
        {
            if (f.Unsafe.TryGetPointer<WeaponRampState>(owner, out var ramp) == false)
                return;

            if (ramp->RampStacks > 0 && weapon->TimeSinceFireReleased > ramp->RampDecayGrace)
            {
                ramp->RampStacks = 0;
            }
        }

        private static void TickKillerInstinct(Frame f, EntityRef owner, FP deltaTime)
        {
            if (f.Unsafe.TryGetPointer<WeaponOnKillReactions>(owner, out var reactions) == false)
                return;

            if (reactions->KillerInstinctTimer > FP._0)
            {
                reactions->KillerInstinctTimer = FPMath.Max(FP._0, reactions->KillerInstinctTimer - deltaTime);
            }
        }

        // Fraction of the magazine consumed INCLUDING the shot about to fire - 1/MagazineSize on
        // the first shot of a fresh magazine, 1 (whole) on the last. Magazine-position perks all
        // read off this instead of tracking their own shot index - Weapon.Ammo already counts down
        // every shot and resets to full on reload, so there's nothing new to track.
        private static FP ResolveMagazineFraction(Weapon* weapon)
        {
            if (weapon->MagazineSize <= 0)
                return FP._1;

            int shotsFiredIncludingThis = weapon->MagazineSize - weapon->Ammo + 1;

            return (FP)shotsFiredIncludingThis / (FP)weapon->MagazineSize;
        }

        private static FP ResolveLiveDamage(Frame f, EntityRef owner, FP baseDamage, FP magazineFraction, bool isLastBullet)
        {
            FP bonus = FP._0;

            if (f.Unsafe.TryGetPointer<WeaponMagazinePositionPerks>(owner, out var magPerks) == true)
            {
                if (magPerks->ExecutionRoundsThreshold > FP._0 && magazineFraction >= FP._1 - magPerks->ExecutionRoundsThreshold)
                {
                    bonus += magPerks->ExecutionRoundsDamageBonus;
                }

                if (isLastBullet == true)
                {
                    bonus += magPerks->FinalRoundDamageBonus;
                }

                bonus += magPerks->EscalatingRoundsMaxDamageBonus * magazineFraction;
            }

            if (f.Unsafe.TryGetPointer<WeaponRampState>(owner, out var ramp) == true)
            {
                bonus += ramp->RampStacks * ramp->RampDamageBonusPerStack;
            }

            return baseDamage * (FP._1 + bonus);
        }

        private static FP ResolveLiveFireCooldown(Frame f, EntityRef entity, Weapon* weapon, WeaponDataAsset weaponData, FP magazineFraction)
        {
            FP fireRateBonus = FP._0;

            if (f.Unsafe.TryGetPointer<WeaponMagazinePositionPerks>(entity, out var magPerks) == true
                && magPerks->OpeningBurstThreshold > FP._0 && magazineFraction <= magPerks->OpeningBurstThreshold)
            {
                fireRateBonus += magPerks->OpeningBurstFireRateBonus;
            }

            if (f.Unsafe.TryGetPointer<WeaponRampState>(entity, out var ramp) == true)
            {
                fireRateBonus += ramp->RampStacks * ramp->RampFireRateBonusPerStack;
            }

            if (f.Unsafe.TryGetPointer<WeaponOnKillReactions>(entity, out var onKill) == true && onKill->KillerInstinctTimer > FP._0)
            {
                fireRateBonus += onKill->KillerInstinctFireRateBonus;
            }

            FP baseCooldown = FP._1 / weaponData.FireRate * weapon->FireCooldownMultiplier / (FP._1 + fireRateBonus);

            return StatUtility.GetFireCooldown(f, entity, baseCooldown);
        }

        // Explosive Sequence's own shot counter - every shot fired (hitscan or projectile) advances
        // it, wrapping back to 0 once it reaches Interval, regardless of whether that particular
        // shot actually connects with anything (mirrors AreaHitData's own "detonates on hit OR
        // expiry" - a proc'd shot that misses still consumed its place in the sequence). procs may
        // be null (no post-impact perk rolled) - a no-op, same as ExplosiveSequenceInterval == 0
        // used to mean before the split.
        private static bool ResolveExplosiveProc(Frame f, WeaponPostImpactProcs* procs)
        {
            if (procs == null || procs->ExplosiveSequenceInterval <= 0)
                return false;

            procs->ShotsSinceExplosiveProc++;

            if (procs->ShotsSinceExplosiveProc < procs->ExplosiveSequenceInterval)
                return false;

            procs->ShotsSinceExplosiveProc = 0;

            // Optional internal cooldown - 0 (every perk's own value) skips this entirely.
            if (procs->ExplosiveSequenceCooldown > FP._0)
            {
                if (procs->ExplosiveSequenceCooldownRemaining > FP._0)
                    return false;
            }

            // Optional proc chance - 0 or >= 1 means "always", which is what the Explosive Sequence
            // perk authors, so its every-Nth-shot behavior is unchanged. Pixie's Explosive Rounds
            // ascension is the consumer that authors a real chance (see PixieExplosiveWeapon).
            if (procs->ExplosiveSequenceChance > FP._0 && procs->ExplosiveSequenceChance < FP._1
                && DamageUtility.RollChance(f, procs->ExplosiveSequenceChance) == false)
                return false;

            if (procs->ExplosiveSequenceCooldown > FP._0)
            {
                procs->ExplosiveSequenceCooldownRemaining = procs->ExplosiveSequenceCooldown;
            }

            return true;
        }

        // Queues a repeat of this exact shot (position/direction/already-resolved damage, not
        // re-evaluated against whatever the ramp/magazine-position stack up to at the time it
        // fires) - Echo Chamber (first 3 shots of every magazine) and Infinite Echo (every shot)
        // both just decide whether to call this, not how it plays out. Silently drops the echo if
        // every slot is already pending (a rapid-fire weapon outrunning EchoDelay) rather than
        // stalling the newest shot waiting for room.
        private static void EnqueueEcho(WeaponEchoState* echoState, FPVector3 position, FPVector3 direction, FP damage)
        {
            var echoes = echoState->PendingEchoes;

            for (int i = 0; i < echoes.Length; i++)
            {
                if (echoes[i].Delay > FP._0)
                    continue;

                echoes[i] = new PendingEcho
                {
                    Delay = echoState->EchoDelay,
                    Position = position,
                    Direction = direction,
                    Damage = damage
                };

                return;
            }
        }

        private static void TickPendingEchoes(Frame f, EntityRef owner, Weapon* weapon, FP deltaTime)
        {
            if (f.Unsafe.TryGetPointer<WeaponEchoState>(owner, out var echoState) == false)
                return;

            var echoes = echoState->PendingEchoes;

            for (int i = 0; i < echoes.Length; i++)
            {
                if (echoes[i].Delay <= FP._0)
                    continue;

                echoes[i].Delay -= deltaTime;

                if (echoes[i].Delay > FP._0)
                    continue;

                FireEcho(f, owner, weapon, echoes[i]);
                echoes[i] = default;
            }
        }

        private static void FireEcho(Frame f, EntityRef owner, Weapon* weapon, PendingEcho echo)
        {
            WeaponDataAsset weaponData = f.FindAsset(weapon->WeaponData);

            switch (weaponData.FireType)
            {
                case WeaponFireType.Hitscan:
                    FireHitscan(f, owner, weapon, weaponData, echo.Damage, echo.Position, echo.Direction, false, false);
                    break;

                case WeaponFireType.Projectile:
                    FireEchoProjectile(f, owner, weapon, weaponData, echo);
                    break;
            }
        }

        private static void FireEchoProjectile(Frame f, EntityRef owner, Weapon* weapon, WeaponDataAsset weaponData, PendingEcho echo)
        {
            if (weaponData.ProjectileData.IsValid == false)
                return;

            ProjectileDataAsset projectileData = f.FindAsset(weaponData.ProjectileData);
            ProjectileMovementData movement = f.FindAsset(projectileData.Movement);

            // Echoes have no locked target (see PendingEcho), so this always replays the free-aim
            // branch of FireProjectile's pellet spread - a shotgun's echo re-fires the whole volley.
            int pelletCount = weaponData.PelletCount > 0 ? weaponData.PelletCount : 1;

            for (int i = 0; i < pelletCount; i++)
            {
                FPVector3 pelletDirection = FPQuaternion.Euler(0, GetPelletAngle(i, pelletCount, weaponData.SpreadAngle), 0) * echo.Direction;
                ProjectileLaunch launch = movement.GetLaunch(f, echo.Position, pelletDirection);

                if (launch.IsValid == false)
                    continue;

                EntityRef entity = ProjectileSpawner.Spawn(f, owner, weaponData.ProjectileData, launch, echo.Damage, DamageSource.Weapon, element: weaponData.Element);
                ApplyProjectilePerks(f, owner, entity, weapon, false, false);
            }
        }

        // Counts down Double Tap's queued extra shot (see PendingDoubleTapShot) the same FP-seconds
        // way TickPendingEchoes counts down echoes. Delay <= 0 means nothing pending, so this is a
        // no-op most ticks.
        private static void TickPendingDoubleTap(Frame f, EntityRef owner, Weapon* weapon, FP deltaTime)
        {
            if (f.Unsafe.TryGetPointer<WeaponFireTimeMods>(owner, out var fireMods) == false
                || fireMods->PendingDoubleTap.Delay <= FP._0)
                return;

            fireMods->PendingDoubleTap.Delay -= deltaTime;

            if (fireMods->PendingDoubleTap.Delay > FP._0)
                return;

            PendingDoubleTapShot pending = fireMods->PendingDoubleTap;
            fireMods->PendingDoubleTap = default;
            FireDoubleTapShot(f, owner, weapon, pending);
        }

        // Replays Double Tap's queued shot through the normal FireShot path (unlike FireEcho, which
        // duplicates FireProjectile/FireHitscan) so IsExplosiveProc/IsCataclysm/GrantPierceAmount still
        // apply exactly like they would have firing synchronously - only the target lock is dropped,
        // same "no locked target" simplification PendingEcho already uses, since spawnPosition/
        // aimDirection are already fully resolved and target/aimAtCenter would just make
        // FireProjectile re-solve an aim point off wherever the target is by the time this fires.
        private static void FireDoubleTapShot(Frame f, EntityRef owner, Weapon* weapon, PendingDoubleTapShot pending)
        {
            WeaponDataAsset weaponData = f.FindAsset(weapon->WeaponData);

            FireShot(f, owner, weapon, weaponData, pending.Damage, pending.SpawnPosition, FP._0, FPVector3.Zero,
                pending.SpawnPosition, pending.AimDirection, EntityRef.None, false, pending.IsExplosiveProc,
                pending.IsCataclysm, pending.GrantPierceAmount);
        }

        // Returns true while the weapon is busy reloading and can't fire this tick.
        private static bool UpdateReload(Frame f, EntityRef entity, Weapon* weapon, bool isFiring)
        {
            if (weapon->ReloadTimer > FP._0)
            {
                weapon->ReloadTimer -= f.DeltaTime;

                if (weapon->ReloadTimer <= FP._0)
                {
                    weapon->ReloadTimer = FP._0;
                    weapon->Ammo = weapon->MagazineSize;
                    f.Events.WeaponReloaded(entity);
                    RevertEmergencyReload(f, entity);
                }

                return true;
            }

            if (weapon->Ammo <= 0)
            {
                StartReload(f, entity, weapon);
                return true;
            }

            if (isFiring)
            {
                weapon->TimeSinceFireReleased = FP._0;
                return false;
            }

            // Tops the magazine back up instantly once the player is out of combat long enough, so
            // a partial magazine isn't carried into the next fight and there's no reload animation
            // to sit through when nothing is shooting back.
            weapon->TimeSinceFireReleased += f.DeltaTime;
            FP autoReloadDelay = FP._0_50 + weapon->ReloadDuration;

            if (weapon->Ammo < weapon->MagazineSize &&
                weapon->TimeSinceFireReleased >= autoReloadDelay)
            {
                weapon->Ammo = weapon->MagazineSize;
                f.Events.WeaponReloaded(entity);
            }

            return false;
        }

        // A weapon authored with ReloadDuration <= 0 gets an instant top-up plus the WeaponReloaded
        // event, not a ReloadTimer that just happens to be very short.
        private static void StartReload(Frame f, EntityRef entity, Weapon* weapon)
        {
            weapon->TimeSinceFireReleased = FP._0;

            ApplyMagazineEmptiedPerks(f, entity);

            if (weapon->ReloadDuration > FP._0)
            {
                weapon->ReloadTimer = StatUtility.GetReloadDuration(f, entity, weapon->ReloadDuration);
                TryApplyEmergencyReload(f, entity);
            }
            else
            {
                weapon->ReloadTimer = FP._0;
                weapon->Ammo = weapon->MagazineSize;
                f.Events.WeaponReloaded(entity);
            }
        }

        // Empty Chamber/Combat Reboot - both react to the magazine emptying, not to the reload
        // itself finishing, so they trigger here regardless of which branch below actually reloads
        // (a timed ReloadTimer or an instant top-up) rather than in UpdateReload's completion path.
        private static void ApplyMagazineEmptiedPerks(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<WeaponReloadHooks>(entity, out var hooks) == false)
                return;

            if (hooks->HasEmptyChamber == true && f.Unsafe.TryGetPointer<Transform3D>(entity, out var transform) == true)
            {
                HitEffectUtility.ApplyShockwave(f, transform->Position, hooks->EmptyChamberRadius, entity, hooks->EmptyChamberKnockback);
            }

            if (hooks->HasCombatReboot == true && f.Unsafe.TryGetPointer<CharacterSkills>(entity, out var skills) == true)
            {
                SkillSystem.ReduceCooldown(f, skills, SkillSlotId.HeroSkill, hooks->CombatRebootCooldownReduction);
            }
        }

        // Only applied for a real (ReloadTimer-driven) reload, not the instant-reload-overdriven
        // case just below (nothing to be mid-reload during) or the idle auto-top-up in
        // UpdateReload (that's not fictionally "reloading" at all). Reverted the moment the real
        // reload actually finishes - see UpdateReload's timer-complete branch.
        private static void TryApplyEmergencyReload(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<WeaponReloadHooks>(entity, out var hooks) == false
                || hooks->HasEmergencyReload == false || hooks->EmergencyReloadApplied == true)
                return;

            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->MoveSpeedMultiplier += hooks->EmergencyReloadMoveSpeedBonus;
            stats->DamageReduction += hooks->EmergencyReloadDamageReduction;
            hooks->EmergencyReloadApplied = true;
        }

        private static void RevertEmergencyReload(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<WeaponReloadHooks>(entity, out var hooks) == false || hooks->EmergencyReloadApplied == false)
                return;

            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == true)
            {
                stats->MoveSpeedMultiplier -= hooks->EmergencyReloadMoveSpeedBonus;
                stats->DamageReduction -= hooks->EmergencyReloadDamageReduction;
            }

            hooks->EmergencyReloadApplied = false;
        }

        // Generic "top this entity's magazine back up right now, as if a reload had just completed" -
        // the reusable primitive behind any one-shot reload grant (Max's Full Throttle rank 3 firing
        // once on the max-Rage crossing today). Deliberately an explicit, event-driven call rather
        // than a live condition consulted inside StartReload, so nothing can repeat it every tick.
        // Also cancels an in-progress reload, since the magazine is full either way.
        public static void RefillMagazine(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<Weapon>(entity, out var weapon) == false)
                return;

            if (weapon->Ammo >= weapon->MagazineSize && weapon->ReloadTimer <= FP._0)
                return;

            weapon->ReloadTimer = FP._0;
            weapon->Ammo = weapon->MagazineSize;
            RevertEmergencyReload(f, entity);
            f.Events.WeaponReloaded(entity);
        }

        // Firing itself is driven by Aim.Target now (auto-attack), not a held Fire input - but a
        // Weapon still needs *something* attaching it to the world (a real player's PlayerLink, or
        // a non-player shooter's InputSource, e.g. Lux's sentry gun) before it's allowed to fire at
        // all. False means the entity has a Weapon but nothing driving it - skip the tick entirely.
        private static bool HasFireDriver(Frame f, EntityRef entity)
        {
            return f.Has<PlayerLink>(entity) == true || f.Has<InputSource>(entity) == true;
        }

        // Hitscan has no movement asset to ask and travels in a straight line by definition, so it
        // meets the body like one - only a Projectile fire type defers to its own movement data.
        private static bool ResolveAimsAtCenter(Frame f, WeaponDataAsset weaponData)
        {
            if (weaponData.FireType != WeaponFireType.Projectile)
                return true;

            return ProjectileAimUtility.ResolveAimsAtCenter(f, weaponData.ProjectileData);
        }

        // How far past a hit a Ricochet bounce looks for its next target - the same search radius
        // DirectHitData.TryRicochet uses for the Projectile version of the perk, so one pick behaves
        // the same on either fire type.
        private static readonly FP HitscanRicochetSearchRadius = 8;

        private static void FireHitscan(Frame f, EntityRef owner, Weapon* weapon, WeaponDataAsset weaponData,
            FP damage, FPVector3 origin, FPVector3 direction, bool isExplosiveProc, bool isCataclysm, int grantPierceAmount = 0)
        {
            FP range = weaponData.Range * weapon->RangeMultiplier;
            int pelletCount = weaponData.PelletCount > 0 ? weaponData.PelletCount : 1;

            // Piercing Rounds/Ricochet, read live rather than baked onto anything: these are the very
            // same WeaponFireTimeMods a Projectile weapon stamps onto every shot it spawns
            // (ApplyProjectilePerks -> Projectile.RemainingPierces/RemainingBounces). A hitscan shot
            // has no entity to stamp, which is why both perks used to do literally nothing here while
            // still being offered by every draw site - a hitscan weapon could spend a pick on a perk
            // it could never use.
            int pierces = 1;
            int bounces = 0;

            if (f.Unsafe.TryGetPointer<WeaponFireTimeMods>(owner, out var mods) == true)
            {
                pierces += mods->BonusPierce;
                bounces += mods->BonusBounces;
            }

            // Kai's Phantom Strike - a one-shot flat pierce bonus consumed once per fired shot, on
            // top of whatever Piercing Rounds already grants. Same value FireProjectile hands to
            // ApplyProjectilePerks.
            pierces += grantPierceAmount;

            // Shared across the whole volley so two pellets landing on one target in the same tick
            // stay distinct events - and now also across the several contacts a single pierced or
            // ricocheted pellet can land. See Events.qtn's comment on EntityDamaged.HitIndex.
            byte hitIndex = 0;

            for (int i = 0; i < pelletCount; i++)
            {
                FPVector3 pelletDirection = FPQuaternion.Euler(0, GetPelletAngle(i, pelletCount, weaponData.SpreadAngle), 0) * direction;

                // Only pellet 0 of a volley procs Explosive Sequence/Cataclysm Round - otherwise an
                // N-pellet shotgun would detonate N explosions off a single trigger pull - or tracks
                // Focused Breach, same "one beam, not N" reasoning.
                FireHitscanPellet(f, owner, weaponData, damage, origin, pelletDirection, range, pierces, bounces,
                    isExplosiveProc && i == 0, isCataclysm && i == 0, isPrimaryPellet: i == 0, ref hitIndex);
            }
        }

        // One pellet's entire path: the initial beam, plus a fresh segment per Ricochet bounce, each
        // segment passing through up to as many entities as Piercing Rounds allows. Deliberately
        // mirrors DirectHitData.ApplyHit's ordering for the Projectile version of the same two perks -
        // Quantum Rounds on every contact, one pierce spent per entity, level geometry ending the shot
        // however much pierce is left, and Explosive Sequence/Cataclysm Round detonating once at
        // wherever the path finally stops.
        private static void FireHitscanPellet(Frame f, EntityRef owner, WeaponDataAsset weaponData,
            FP damage, FPVector3 origin, FPVector3 direction, FP range, int pierces, int bounces,
            bool isExplosiveProc, bool isCataclysm, bool isPrimaryPellet, ref byte hitIndex)
        {
            FPVector3 segmentOrigin = origin;
            FPVector3 segmentDirection = direction;
            FP remainingRange = range;
            int remainingPierces = pierces;
            bool trackedFocusedBreach = false;

            // Bounded by construction - the initial beam, plus one iteration per Ricochet bounce
            // (capped by `bounces` below), plus at most one pierce-flatten per contact that actually
            // spent a pierce (capped by `pierces`, and never repeatable on an already-level segment) -
            // so neither continuation can loop forever however the target search resolves.
            int usedBounces = 0;
            int maxSegments = bounces + pierces + 1;

            for (int segment = 0; segment < maxSegments; segment++)
            {
                FPVector3 endPoint = segmentOrigin + segmentDirection * remainingRange;
                FP travelled = remainingRange;
                bool didHit = false;
                EntityRef bounceFrom = EntityRef.None;

                // The entity a surviving pierce should carry the beam on FROM, level, rather than
                // continuing to dive into the floor behind it - see the pierce block below.
                EntityRef flattenFrom = EntityRef.None;

                // What this segment finally landed on, for the view's own impact effect - the LAST
                // entity it damaged, since that is the one sitting at endPoint. Stays None when the
                // segment ended on level geometry or simply ran out of range.
                EntityRef segmentTarget = EntityRef.None;

                var hits = f.Physics3D.RaycastAll(segmentOrigin, segmentDirection, remainingRange, -1, QueryOptions.HitAll);

                // Walked in cast order off CastDistanceNormalized rather than via
                // HitCollection3D.Sort, which orders by Hit3D.Point - and Point only holds real data
                // when the query passes QueryOptions.ComputeDetailedInfo, which this one deliberately
                // doesn't (see ResolveHitscanPoint). O(n^2) over the handful of hits one short ray
                // collects, and stable on ties since it keeps the first index at equal distance.
                bool* consumed = stackalloc bool[hits.Count > 0 ? hits.Count : 1];

                for (int step = 0; step < hits.Count; step++)
                {
                    int nearest = -1;
                    FP nearestDistance = FP.MaxValue;

                    for (int i = 0; i < hits.Count; i++)
                    {
                        if (consumed[i] == true)
                            continue;

                        FP candidate = hits[i].CastDistanceNormalized;

                        if (nearest >= 0 && candidate >= nearestDistance)
                            continue;

                        nearest = i;
                        nearestDistance = candidate;
                    }

                    if (nearest < 0)
                        break;

                    consumed[nearest] = true;

                    EntityRef hitEntity = hits[nearest].Entity;

                    if (hitEntity == owner)
                        continue;

                    FP distance = remainingRange * nearestDistance;

                    didHit = true;
                    endPoint = ResolveHitscanPoint(f, hitEntity, segmentOrigin, segmentDirection, distance);
                    travelled = distance;

                    if (isPrimaryPellet == true && trackedFocusedBreach == false)
                    {
                        trackedFocusedBreach = true;
                        TryApplyFocusedBreach(f, owner, hitEntity);
                    }

                    // Level geometry ends the shot outright, whatever pierce is left - the same rule
                    // DirectHitData.ApplyHit applies to its own EntityRef.None hit, except that
                    // "geometry" here also covers the entity-backed kind (see IsHitscanTarget).
                    if (IsHitscanTarget(f, hitEntity) == false)
                        break;

                    ApplyHitscanHit(f, owner, weaponData, hitEntity, endPoint, damage, ref hitIndex);
                    segmentTarget = hitEntity;

                    remainingPierces--;

                    if (remainingPierces > 0)
                    {
                        // A muzzle held above the shooter (CharacterData.WeaponPosition) firing at a
                        // target's collider center makes this beam a DOWNWARD one, so its remaining
                        // pierces would be spent on the floor a step past this enemy instead of on
                        // the ones behind it - the hitscan half of DirectHitData's own
                        // FlattenTrajectoryOnPierce. Re-cast level from here instead, keeping the
                        // pierces and the range this beam has left.
                        if (ProjectileAimUtility.TryFlattenHeading(segmentDirection, out _) == true)
                        {
                            flattenFrom = hitEntity;
                            break;
                        }

                        continue; // Piercing Rounds - carry on through this same segment
                    }

                    bounceFrom = hitEntity;
                    break;
                }

                // A segment that ends on a surviving pierce is carried on from that enemy's own
                // collider CENTRE (see the levelling block below), so the leg drawn to it ends there
                // too rather than at the enemy's transform origin - its feet. Keeps the drawn path
                // contiguous for the views that stitch legs together on their own ChainTolerance
                // (ParticleHitscanView/ContinuousHitscanView), and puts the contact at body height,
                // which is where the beam actually passed through.
                if (flattenFrom != EntityRef.None
                    && ProjectileAimUtility.TryGetAimPoint(f, flattenFrom, aimAtCenter: true, out FPVector3 pierceCenter) == true)
                {
                    endPoint = pierceCenter;
                }

                // Hitscan never spawns an entity (unlike Projectile, which the view tracks via
                // ProjectileDestroyed) - this is the only view hook for a hitscan pellet's tracer/
                // impact VFX, see HitscanViewBase and its styles. Raised per SEGMENT, so a Ricochet
                // bounce draws its own leg of the path with no view-side changes at all.
                f.Events.HitscanFired(owner, segmentOrigin, endPoint, didHit, segmentTarget);

                if (isPrimaryPellet == true && didHit == false && segment == 0)
                {
                    ResetFocusedBreach(f, owner);
                }

                FP nextRange = remainingRange - travelled;

                // Carries the beam on from the enemy it just pierced, level - endPoint is that
                // enemy's collider centre by the time it gets here (see above), not the surface point
                // the ray clipped: body height is where the enemies behind it are, and a ray never
                // reports the collider its own origin sits inside of, so the beam can't re-hit what it
                // just pierced (the same property a Ricochet bounce off endPoint already relies on).
                // Deliberately does not spend a bounce; this is the same beam, just level.
                if (flattenFrom != EntityRef.None && nextRange > FP._0
                    && ProjectileAimUtility.TryFlattenHeading(segmentDirection, out FPVector3 levelDirection) == true)
                {
                    segmentOrigin = endPoint;
                    segmentDirection = levelDirection.Normalized;
                    remainingRange = nextRange;
                    continue;
                }

                // Ricochet - redirects from the entity that spent the last pierce toward the nearest
                // other enemy, exactly as DirectHitData.TryRicochet does for a projectile: no
                // reflection off a surface normal this has no information about, and no bounce at all
                // when there is nothing else in range to bounce to. nextRange carries the shot's
                // remaining engagement range across the bounce, the same way a ricocheting projectile
                // keeps spending its own MaxTravelDistance budget rather than getting a fresh one.
                if (bounceFrom != EntityRef.None && usedBounces < bounces && nextRange > FP._0
                    && TryFindRicochetTarget(f, endPoint, bounceFrom, out var targetPosition) == true)
                {
                    FPVector3 toTarget = targetPosition - endPoint;

                    if (toTarget.SqrMagnitude > FP._0)
                    {
                        usedBounces++;
                        segmentOrigin = endPoint;
                        segmentDirection = toTarget.Normalized;
                        remainingRange = nextRange;
                        remainingPierces = 1; // one contact per bounce, same as a ricocheting projectile
                        continue;
                    }
                }

                ApplyHitscanTerminalPerks(f, owner, endPoint, damage, isExplosiveProc, isCataclysm);
                return;
            }
        }

        // Hit3D.Point only reads real data when the query passes QueryOptions.ComputeDetailedInfo
        // (see ProjectileSystem.ResolveHitPoint's own comment on this) - these raycasts don't, so the
        // point has to be reconstructed.
        //
        // Only a real TARGET resolves to its own Transform3D: an enemy or a breakable is a point-ish
        // body, and terminating the shot at its center reads better than at whatever surface point
        // the ray happened to clip. Level geometry is the exact opposite - some of it in this project
        // is a genuinely dynamic ENTITY whose Transform3D is the whole chunk's PIVOT, which can sit
        // tens of units from where the beam actually met the wall, so a tracer drawn to it shoots off
        // to a point in space with nothing there. That is what "the line goes nowhere on a wall hit"
        // was: everything else about the shot was right, just this one position. Distance along the
        // ray is the only sane answer for anything that isn't a target.
        private static FPVector3 ResolveHitscanPoint(Frame f, EntityRef hitEntity, FPVector3 origin, FPVector3 direction, FP distance)
        {
            if (IsHitscanTarget(f, hitEntity) == true && f.Unsafe.TryGetPointer<Transform3D>(hitEntity, out var hitTransform) == true)
                return hitTransform->Position;

            return origin + direction * distance;
        }

        // What a beam can shoot INTO versus what stops it dead. Mirrors ProjectileHitData's own
        // ShouldDetonate/IsCombatant split, which matters far more now that a beam can pierce: some
        // level geometry in this project is a genuinely dynamic ENTITY (see EnemyMovementUtility's
        // own notes on this), so an EntityRef.None test alone would let Piercing Rounds shoot a
        // hitscan weapon straight through a wall. Only a real target consumes a pierce and lets the
        // beam carry on; everything else ends it where it stands.
        private static bool IsHitscanTarget(Frame f, EntityRef entity)
        {
            return entity != EntityRef.None
                && (f.Has<Enemy>(entity) == true || f.Has<PlayerLink>(entity) == true || f.Has<Breakable>(entity) == true);
        }

        private static bool TryFindRicochetTarget(Frame f, FPVector3 point, EntityRef exclude, out FPVector3 targetPosition)
        {
            targetPosition = default;

            if (WeaponPerkUtility.TryFindNearestEnemy(f, point, HitscanRicochetSearchRadius, exclude, out var target) == false)
                return false;

            if (f.Unsafe.TryGetPointer<Transform3D>(target, out var targetTransform) == false)
                return false;

            targetPosition = targetTransform->Position;
            return true;
        }

        // Everything one hitscan contact does to a single target: damage, the weapon's own element,
        // Element Infusion, and Quantum Rounds. Split out of FireHitscan once Piercing Rounds and
        // Ricochet made "one shot, one contact" stop being true, and internal so Critical Rebound's
        // own hitscan bounce (WeaponPerkReactionSystem) lands exactly like a normal contact does.
        internal static void ApplyHitscanHit(Frame f, EntityRef owner, WeaponDataAsset weaponData,
            EntityRef hitEntity, FPVector3 point, FP damage, ref byte hitIndex)
        {
            // hitIndex: contacts sharing a target/damage/tick would otherwise hash-collide and get
            // silently collapsed by Quantum's event dedup - see Events.qtn's comment on
            // EntityDamaged.HitIndex.
            DamageUtility.ApplyDamage(f, hitEntity, damage, owner, DamageSource.Weapon, hitIndex: hitIndex);
            hitIndex++;

            Log.Debug($"[Weapon] Hitscan from {owner} hit {hitEntity} for {damage} base damage");

            // Hitscan has no Effects list to run through HitEffectUtility (unlike ProjectileHitData/
            // AreaDamage), so this is called directly here instead. Snapshot pre-hit Rift Mark stacks
            // the same way HitEffectUtility.ApplyToTarget does - see
            // HitEffectContext.PreHitRiftMarkStacks' own comment.
            byte preHitRiftMarkStacks = StatusEffectUtility.GetRiftMarkStacks(f, hitEntity);
            StatusEffectUtility.TryApplyElementalStatus(f, hitEntity, owner, DamageSource.Weapon,
                weaponData.Element, damage, preHitRiftMarkStacks);

            // Element Infusion perk (WeaponElementInfusion) - hitscan has no projectile to carry
            // PerkElement on, so read it live off the owner here and apply the extra element with
            // its own proc chance, sharing the same pre-hit Rift Mark snapshot as the base call.
            if (f.Unsafe.TryGetPointer<WeaponElementInfusion>(owner, out var infusion) == true)
            {
                StatusEffectUtility.TryApplyInfusedElement(f, hitEntity, owner, DamageSource.Weapon,
                    infusion->Element, infusion->ProcChance, damage, preHitRiftMarkStacks);
            }

            ApplyHitscanQuantumRounds(f, owner, hitEntity, point, damage);
        }


        // Cone spread around the aim direction, same convention as
        // FanProjectileDeliveryData.Begin's non-Radial branch: pellet 0 sits at -SpreadAngle/2, the
        // last pellet at +SpreadAngle/2, evenly stepped between. A single pellet always returns 0
        // (no spread) regardless of SpreadAngle.
        private static FP GetPelletAngle(int index, int pelletCount, FP spreadAngle)
        {
            if (pelletCount <= 1)
                return FP._0;

            FP step = spreadAngle / (pelletCount - 1);
            return -spreadAngle / 2 + step * index;
        }

        // Quantum Rounds' Hitscan equivalent - a Hitscan weapon has no Projectile entity for it to
        // hook off of (see DirectHitData.ApplyQuantumRounds for the Projectile-type version), so it
        // applies directly here instead, synchronously with the raycast contact itself. Every
        // contact, not just the terminal one - "hits damage an additional nearby enemy" reads as a
        // per-hit effect, not a per-shot one, same as it does for a piercing projectile.
        private static void ApplyHitscanQuantumRounds(Frame f, EntityRef owner, EntityRef hitEntity, FPVector3 point, FP damage)
        {
            if (f.Unsafe.TryGetPointer<WeaponPostImpactProcs>(owner, out var procs) == false || procs->HasQuantumRounds == false)
                return;

            if (WeaponPerkUtility.TryFindNearestEnemy(f, point, procs->QuantumRoundsRadius, hitEntity, out var nearby) == false)
                return;

            DamageUtility.ApplyDamage(f, nearby, damage * procs->QuantumRoundsDamageMultiplier, owner, DamageSource.Weapon);

            FPVector3 targetPosition = f.Unsafe.TryGetPointer<Transform3D>(nearby, out var nearbyTransform) == true
                ? nearbyTransform->Position
                : point;

            f.Events.QuantumRoundsTriggered(nearby, targetPosition, procs->QuantumRoundsSource);
        }

        // Explosive Sequence/Cataclysm Round's Hitscan equivalent, fired once at wherever the pellet's
        // whole path ended - through every pierce and every Ricochet bounce, mirroring
        // DirectHitData.ApplyTerminalWeaponPerks, which only runs when the projectile is finally
        // spent. Detonates on level geometry too, same as that one does.
        private static void ApplyHitscanTerminalPerks(Frame f, EntityRef owner, FPVector3 point, FP damage,
            bool isExplosiveProc, bool isCataclysm)
        {
            if (isExplosiveProc == false && isCataclysm == false)
                return;

            if (f.Unsafe.TryGetPointer<WeaponPostImpactProcs>(owner, out var procs) == false)
                return;

            // Bigger Boom (Pixie passive ascension) - read live rather than baked into the weapon at
            // equip time, so picking (or ranking up) Bigger Boom mid-run scales every explosion
            // immediately, and multiple ranks compound off the same unscaled base radius instead of
            // off an already-scaled one. No-op (multiplier 1) for every hero without it - see
            // DamageUtility.ResolvePixieExplosionRadiusMultiplier.
            // Skill Area (CharacterStats.AreaRadiusMultiplier) folded in alongside Bigger Boom so it
            // scales these weapon explosions (Cataclysm Round / Explosive Sequence) too, matching the
            // bomb/skill blasts - 1x for anyone without it.
            FP radiusMultiplier = DamageUtility.ResolvePixieExplosionRadiusMultiplier(f, owner) * StatUtility.GetAreaMultiplier(f, owner);

            if (isCataclysm == true)
            {
                FP radius = procs->CataclysmRadius * radiusMultiplier;
                HitEffectUtility.ApplyExplosion(f, point, radius, owner,
                    damage * procs->CataclysmDamageMultiplier, DamageSource.Weapon);
                WeaponPerkUtility.TryApplyUnstablePayloadMarks(f, point, radius, owner);
            }
            else
            {
                FP radius = procs->ExplosiveSequenceRadius * radiusMultiplier;
                HitEffectUtility.ApplyExplosion(f, point, radius, owner,
                    damage * procs->ExplosiveSequenceDamageMultiplier, DamageSource.Weapon);
                WeaponPerkUtility.TryApplyUnstablePayloadMarks(f, point, radius, owner);
            }
        }


        // Focused Breach (see docs/weapon-perks.md) - simulates "beam contact" as continuous same-
        // target Hitscan hits, since this project has no dedicated Beam fire type. Losing contact (a
        // miss, or the hit entity changing) resets progress via ResetFocusedBreach below; only pellet
        // 0 of a volley tracks it, same "one beam, not N" reasoning FireHitscan's own
        // Explosive Sequence/Cataclysm Round gating uses.
        private static void TryApplyFocusedBreach(Frame f, EntityRef owner, EntityRef hitEntity)
        {
            if (f.Unsafe.TryGetPointer<WeaponHitTrackingPerks>(owner, out var tracking) == false || tracking->HasFocusedBreach == false)
                return;

            if (tracking->FocusedBreachTarget != hitEntity)
            {
                tracking->FocusedBreachTarget = hitEntity;
                tracking->FocusedBreachContactTime = FP._0;
            }

            tracking->FocusedBreachContactTime += f.DeltaTime;

            if (tracking->FocusedBreachContactTime < tracking->FocusedBreachThreshold)
                return;

            tracking->FocusedBreachContactTime = FP._0;

            ElementalReactionConfig config = StatusEffectUtility.GetElementalReactionConfig(f);

            if (config == null || hitEntity == EntityRef.None || f.Has<Enemy>(hitEntity) == false)
                return;

            if (f.Unsafe.TryGetPointer<StatusEffects>(hitEntity, out var status) == false)
                return;

            if (RiftMarkApplicationUtility.TryConsumeCooldown(status, RiftMarkCooldownKey.FocusedBreach, config.StandardMarkApplicationCooldown) == false)
                return;

            var request = new RiftMarkApplicationRequest
            {
                Source = owner,
                Target = hitEntity,
                HitSequence = f.Number,
                ApplicationSource = RiftMarkApplicationSource.WeaponPerkFocusedBreach,
                RequestedStacks = config.StacksAppliedPerApplication,
                Owner = owner,
                CooldownKey = RiftMarkCooldownKey.FocusedBreach,
            };

            RiftMarkApplicationUtility.ApplyRequest(f, request, config);
        }

        private static void ResetFocusedBreach(Frame f, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<WeaponHitTrackingPerks>(owner, out var tracking) == false || tracking->HasFocusedBreach == false)
                return;

            tracking->FocusedBreachTarget = EntityRef.None;
            tracking->FocusedBreachContactTime = FP._0;
        }

        private static void FireProjectile(Frame f, EntityRef owner, Weapon* weapon, WeaponDataAsset weaponData,
            FP damage, FPVector3 casterPosition, FP aimAngle, FPVector3 holdOffset, FPVector3 spawnPosition, FPVector3 aimDirection,
            EntityRef target, bool aimAtCenter, bool isExplosiveProc, bool isCataclysm, int grantPierceAmount = 0)
        {
            ProjectileDataAsset projectileData = f.FindAsset(weaponData.ProjectileData);
            ProjectileMovementData movement = f.FindAsset(projectileData.Movement);

            // A locked target is solved toward as a point rather than a direction - a lob needs the
            // real distance to land on the target instead of a fixed TargetDistance down the aim ray.
            // Resolved once for the whole volley; each pellet below rotates this same origin/point
            // pair per-pellet rather than re-resolving the aim point from scratch.
            bool hasAimPoint = ProjectileAimUtility.TryGetAimPoint(f, target, aimAtCenter, out FPVector3 aimPoint);
            FPVector3 resolvedOrigin = hasAimPoint
                ? ProjectileSpawner.ResolveSpawnOrigin(casterPosition, aimPoint, aimAngle, weaponData.SpawnAnchor, weaponData.SpawnOffset) + holdOffset
                : spawnPosition;
            FPVector3 delta = hasAimPoint ? aimPoint - resolvedOrigin : FPVector3.Zero;

            int pelletCount = weaponData.PelletCount > 0 ? weaponData.PelletCount : 1;

            for (int i = 0; i < pelletCount; i++)
            {
                FPQuaternion pelletRotation = FPQuaternion.Euler(0, GetPelletAngle(i, pelletCount, weaponData.SpreadAngle), 0);

                ProjectileLaunch launch = hasAimPoint
                    ? movement.GetLaunchToTarget(f, resolvedOrigin, resolvedOrigin + pelletRotation * delta, target)
                    : movement.GetLaunch(f, spawnPosition, pelletRotation * aimDirection);

                if (launch.IsValid == false)
                {
                    Log.Error($"[Weapon] {owner} resolved no valid launch for pellet {i} toward {target} - skipped");
                    continue;
                }

                EntityRef entity = ProjectileSpawner.Spawn(f, owner, weaponData.ProjectileData, launch, damage, DamageSource.Weapon,
                    target: target, element: weaponData.Element, pelletIndex: i);

                // Only pellet 0 of a volley procs Explosive Sequence/Cataclysm Round - see FireHitscan.
                // Phantom Strike's bonus pierce is NOT pellet-0-gated - "your next shot pierces" reads
                // as the whole shot, every pellet, not just one.
                ApplyProjectilePerks(f, owner, entity, weapon, isExplosiveProc && i == 0, isCataclysm && i == 0, grantPierceAmount);

                Log.Debug($"[Weapon] Spawned pellet {i}/{pelletCount} from {owner} with velocity {launch.Velocity}");
            }
        }

        // Bakes this shot's weapon-perk state onto the just-spawned Projectile - Piercing
        // Rounds/Ricochet (RemainingPierces/RemainingBounces), Long Barrel/engagement range
        // (MaxTravelDistance, see Projectile.qtn), and this specific shot's Explosive
        // Sequence/Cataclysm Round flags (see DirectHitData for how the latter two are consumed on
        // impact). grantPierceAmount is Kai's Phantom Strike - a one-shot flat pierce bonus (1/2/99 per
        // rank) baked on top of whatever Piercing Rounds already grants, consumed once per fired shot
        // back in Update, not per pellet.
        private static void ApplyProjectilePerks(Frame f, EntityRef owner, EntityRef entity, Weapon* weapon, bool isExplosiveProc, bool isCataclysm, int grantPierceAmount = 0)
        {
            if (f.Unsafe.TryGetPointer<Projectile>(entity, out var projectile) == false)
                return;

            if (f.Unsafe.TryGetPointer<WeaponFireTimeMods>(owner, out var mods) == true)
            {
                projectile->RemainingPierces += mods->BonusPierce;
                projectile->RemainingBounces += mods->BonusBounces;
            }

            projectile->RemainingPierces += grantPierceAmount;

            projectile->MaxTravelDistance = WeaponPerkUtility.ResolveWeaponRange(f, weapon);
            projectile->IsExplosiveProc = isExplosiveProc;
            projectile->IsCataclysm = isCataclysm;

            // Element Infusion perk (WeaponElementInfusion) - carry the extra element + its own proc
            // chance to impact, applied via StatusEffectUtility.TryApplyInfusedElement alongside the
            // projectile's native Element. Absent component leaves both at Neutral/0, a no-op.
            if (f.Unsafe.TryGetPointer<WeaponElementInfusion>(owner, out var infusion) == true)
            {
                projectile->PerkElement = infusion->Element;
                projectile->PerkElementChance = infusion->ProcChance;
            }
        }

        // PlayerLink deliberately isn't a required field here anymore - a real player has one, but a
        // non-player shooter (Lux's sentry gun) doesn't, and shouldn't need one just to be seen by
        // this system. See HasFireDriver, which checks for PlayerLink/InputSource itself instead.
        public struct Filter
        {
            public EntityRef Entity;
            public Weapon* Weapon;
            public Transform3D* Transform3D;
            public Aim* Aim;
        }
    }
}
