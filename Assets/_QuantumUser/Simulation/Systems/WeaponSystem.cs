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

            SeedStats(f, weapon);
            ApplyPerks(f, weapon);
            ApplyPixieExplosiveWeapon(f, owner, weapon);

            weapon->Ammo = weapon->MagazineSize;
            weapon->FireCooldownTimer = FP._0;
            weapon->ReloadTimer = FP._0;
            weapon->TimeSinceFireReleased = FP._0;
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

            weapon->ExplosiveSequenceInterval = 1;
            weapon->ExplosiveSequenceRadius = FPMath.Max(weapon->ExplosiveSequenceRadius, explosive->Radius);
            weapon->ExplosiveSequenceDamageMultiplier = FPMath.Max(weapon->ExplosiveSequenceDamageMultiplier, explosive->DamageMultiplier);
        }

        // Every perk-mutable stat starts life as its authored value; perks then edit these in place.
        // Damage/FireCooldown are the exception - see Weapon.qtn - so their multipliers reset to 1
        // here instead of copying an asset value.
        private static void SeedStats(Frame f, Weapon* weapon)
        {
            WeaponDataAsset weaponData = f.FindAsset(weapon->WeaponData);

            weapon->DamageMultiplier = FP._1;
            weapon->FireCooldownMultiplier = FP._1;
            weapon->RangeMultiplier = FP._1;
            weapon->MagazineSize = weaponData.MagazineSize;
            weapon->ReloadDuration = weaponData.ReloadDuration;
            weapon->CriticalChance = weaponData.CriticalChance;
            weapon->CriticalDamageBonus = weaponData.CriticalDamageBonus;

            SeedPerkRoster(weapon);
        }

        // None of these have an authored value on WeaponDataAsset to seed from - they only ever
        // come from a perk's own Apply - so they're reset to their "no effect" default here instead,
        // extending the same "SeedStats resets everything before ApplyPerks re-bakes it" invariant
        // Equip's own doc comment already promises to the wider perk roster (see
        // docs/weapon-perks.md), so a weapon swap can't carry over a previous roll's perk state.
        private static void SeedPerkRoster(Weapon* weapon)
        {
            weapon->OpeningBurstFireRateBonus = FP._0;
            weapon->OpeningBurstThreshold = FP._0;
            weapon->ExecutionRoundsDamageBonus = FP._0;
            weapon->ExecutionRoundsThreshold = FP._0;
            weapon->FinalRoundDamageBonus = FP._0;
            weapon->EscalatingRoundsMaxDamageBonus = FP._0;

            weapon->RampStacks = 0;
            weapon->RampMaxStacks = 0;
            weapon->RampDamageBonusPerStack = FP._0;
            weapon->RampFireRateBonusPerStack = FP._0;
            weapon->RampDecayGrace = FP._0;

            weapon->BonusPierce = 0;
            weapon->BonusBounces = 0;
            weapon->DoubleTapChance = FP._0;

            weapon->HasSplitShot = false;
            weapon->SplitShotCount = 0;
            weapon->SplitShotDamageMultiplier = FP._0;

            weapon->HasQuantumRounds = false;
            weapon->QuantumRoundsRadius = FP._0;
            weapon->QuantumRoundsDamageMultiplier = FP._0;

            weapon->ExplosiveSequenceInterval = 0;
            weapon->ExplosiveSequenceRadius = FP._0;
            weapon->ExplosiveSequenceDamageMultiplier = FP._0;
            weapon->ShotsSinceExplosiveProc = 0;

            weapon->HasCataclysmRound = false;
            weapon->CataclysmRadius = FP._0;
            weapon->CataclysmDamageMultiplier = FP._0;

            weapon->HasEchoChamber = false;
            weapon->HasInfiniteEcho = false;
            weapon->EchoDelay = FP._0;

            var echoes = weapon->PendingEchoes;

            for (int i = 0; i < echoes.Length; i++)
            {
                echoes[i] = default;
            }

            weapon->HasEmptyChamber = false;
            weapon->EmptyChamberRadius = FP._0;
            weapon->EmptyChamberKnockback = FP._0;

            weapon->HasCombatReboot = false;
            weapon->CombatRebootCooldownReduction = FP._0;

            weapon->HasPredatorMagazine = false;
            weapon->PredatorMagazineRestoreFraction = FP._0;

            weapon->HasEmergencyReload = false;
            weapon->EmergencyReloadMoveSpeedBonus = FP._0;
            weapon->EmergencyReloadDamageReduction = FP._0;
            weapon->EmergencyReloadApplied = false;

            weapon->KillerInstinctFireRateBonus = FP._0;
            weapon->KillerInstinctDuration = FP._0;
            weapon->KillerInstinctTimer = FP._0;

            weapon->CritAmmoRestoreChance = FP._0;
            weapon->CritAmmoRestoreAmount = 0;

            weapon->HasCriticalRebound = false;
            weapon->CriticalReboundRadius = FP._0;
            weapon->CriticalReboundDamageMultiplier = FP._0;
        }

        private static void ApplyPerks(Frame f, Weapon* weapon)
        {
            var perks = weapon->Perks;

            for (int i = 0; i < perks.Length; ++i)
            {
                if (perks[i].IsValid == false)
                    continue;

                f.FindAsset(perks[i]).Apply(f, weapon);
            }

            Log.Debug($"[Weapon] equipped - damageMultiplier={weapon->DamageMultiplier}, " +
                $"cooldownMultiplier={weapon->FireCooldownMultiplier}, magazine={weapon->MagazineSize}, crit={weapon->CriticalChance}");
        }

        // Grants a perk after the fact (a level-up, not a drop roll) - records it so UI can read the
        // roll back, then bakes it. False when every slot is already taken.
        public static bool AddPerk(Frame f, Weapon* weapon, AssetRef<WeaponPerkData> perkRef)
        {
            if (perkRef.IsValid == false)
                return false;

            var perks = weapon->Perks;

            for (int i = 0; i < perks.Length; ++i)
            {
                if (perks[i].IsValid)
                    continue;

                perks[i] = perkRef;
                f.FindAsset(perkRef).Apply(f, weapon);

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

            if (AddPerk(f, filter.Weapon, command.Perk) == true)
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
            TickRamp(filter.Weapon);
            TickKillerInstinct(filter.Weapon, f.DeltaTime);
            TickPendingEchoes(f, filter.Entity, filter.Weapon, f.DeltaTime);

            if (StatusEffectUtility.IsStunned(f, filter.Entity) == true)
                return;

            if (HasFireDriver(f, filter.Entity) == false)
                return;

            // Auto-attack: firing is gated on Aim.Target (already re-resolved every tick by
            // AimSystem/SentryBarrelSystem) rather than a held Fire input - whoever's holding this
            // weapon shoots on its own the moment a target is in range, no button needed.
            bool hasTarget = filter.Aim->Target != EntityRef.None;

            if (UpdateReload(f, filter.Entity, filter.Weapon, hasTarget))
                return;

            if (hasTarget == false || filter.Weapon->FireCooldownTimer > FP._0)
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
            bool isExplosiveProc = ResolveExplosiveProc(filter.Weapon);
            bool isCataclysm = filter.Weapon->HasCataclysmRound == true && isLastBullet == true;

            // Read fresh off the asset every fire, not a baked stat - see Weapon.qtn - so tuning
            // WeaponDataAsset.Damage/FireRate in the Inspector applies immediately to
            // already-equipped weapons instead of only the next Equip.
            FP damage = ResolveLiveDamage(filter.Weapon, weaponData.Damage * filter.Weapon->DamageMultiplier, magazineFraction, isLastBullet);

            FireShot(f, filter.Entity, filter.Weapon, weaponData, damage, casterPosition, aimAngle, holdOffset,
                spawnPosition, aimDirection, filter.Aim->Target, aimAtCenter, isExplosiveProc, isCataclysm);

            if (filter.Weapon->DoubleTapChance > FP._0 && DamageUtility.RollChance(f, filter.Weapon->DoubleTapChance) == true)
            {
                FireShot(f, filter.Entity, filter.Weapon, weaponData, damage, casterPosition, aimAngle, holdOffset,
                    spawnPosition, aimDirection, filter.Aim->Target, aimAtCenter, isExplosiveProc, isCataclysm);
            }

            if (filter.Weapon->HasInfiniteEcho == true || (filter.Weapon->HasEchoChamber == true && isEchoEligibleShot == true))
            {
                EnqueueEcho(filter.Weapon, spawnPosition, aimDirection, damage);
            }

            filter.Weapon->FireCooldownTimer = ResolveLiveFireCooldown(f, filter.Entity, filter.Weapon, weaponData, magazineFraction);
            filter.Weapon->Ammo--;
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
            EntityRef target, bool aimAtCenter, bool isExplosiveProc, bool isCataclysm)
        {
            switch (weaponData.FireType)
            {
                case WeaponFireType.Hitscan:
                    FireHitscan(f, owner, weapon, weaponData, damage, spawnPosition, aimDirection, isExplosiveProc, isCataclysm);
                    break;

                case WeaponFireType.Projectile:
                    FireProjectile(f, owner, weapon, weaponData, damage, casterPosition, aimAngle, holdOffset, spawnPosition, aimDirection,
                        target, aimAtCenter, isExplosiveProc, isCataclysm);
                    break;
            }
        }

        // Decays the shared ramp pool (Relentless Fire/Suppressive Cycle/Overcharge Cycle) back to
        // 0 once the wielder has held fire released past RampDecayGrace - a snap reset rather than
        // a gradual per-tick drain, same "read live every shot, nothing to revert" idiom the pool
        // already uses for its bonus math (see ResolveLiveDamage/ResolveLiveFireCooldown).
        private static void TickRamp(Weapon* weapon)
        {
            if (weapon->RampStacks > 0 && weapon->TimeSinceFireReleased > weapon->RampDecayGrace)
            {
                weapon->RampStacks = 0;
            }
        }

        private static void TickKillerInstinct(Weapon* weapon, FP deltaTime)
        {
            if (weapon->KillerInstinctTimer > FP._0)
            {
                weapon->KillerInstinctTimer = FPMath.Max(FP._0, weapon->KillerInstinctTimer - deltaTime);
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

        private static FP ResolveLiveDamage(Weapon* weapon, FP baseDamage, FP magazineFraction, bool isLastBullet)
        {
            FP bonus = FP._0;

            if (weapon->ExecutionRoundsThreshold > FP._0 && magazineFraction >= FP._1 - weapon->ExecutionRoundsThreshold)
            {
                bonus += weapon->ExecutionRoundsDamageBonus;
            }

            if (isLastBullet == true)
            {
                bonus += weapon->FinalRoundDamageBonus;
            }

            bonus += weapon->EscalatingRoundsMaxDamageBonus * magazineFraction;
            bonus += weapon->RampStacks * weapon->RampDamageBonusPerStack;

            return baseDamage * (FP._1 + bonus);
        }

        private static FP ResolveLiveFireCooldown(Frame f, EntityRef entity, Weapon* weapon, WeaponDataAsset weaponData, FP magazineFraction)
        {
            FP fireRateBonus = FP._0;

            if (weapon->OpeningBurstThreshold > FP._0 && magazineFraction <= weapon->OpeningBurstThreshold)
            {
                fireRateBonus += weapon->OpeningBurstFireRateBonus;
            }

            fireRateBonus += weapon->RampStacks * weapon->RampFireRateBonusPerStack;

            if (weapon->KillerInstinctTimer > FP._0)
            {
                fireRateBonus += weapon->KillerInstinctFireRateBonus;
            }

            FP baseCooldown = FP._1 / weaponData.FireRate * weapon->FireCooldownMultiplier / (FP._1 + fireRateBonus);

            return StatUtility.GetFireCooldown(f, entity, baseCooldown);
        }

        // Explosive Sequence's own shot counter - every shot fired (hitscan or projectile) advances
        // it, wrapping back to 0 once it reaches Interval, regardless of whether that particular
        // shot actually connects with anything (mirrors AreaHitData's own "detonates on hit OR
        // expiry" - a proc'd shot that misses still consumed its place in the sequence).
        private static bool ResolveExplosiveProc(Weapon* weapon)
        {
            if (weapon->ExplosiveSequenceInterval <= 0)
                return false;

            weapon->ShotsSinceExplosiveProc++;

            if (weapon->ShotsSinceExplosiveProc < weapon->ExplosiveSequenceInterval)
                return false;

            weapon->ShotsSinceExplosiveProc = 0;
            return true;
        }

        // Queues a repeat of this exact shot (position/direction/already-resolved damage, not
        // re-evaluated against whatever the ramp/magazine-position stack up to at the time it
        // fires) - Echo Chamber (first 3 shots of every magazine) and Infinite Echo (every shot)
        // both just decide whether to call this, not how it plays out. Silently drops the echo if
        // every slot is already pending (a rapid-fire weapon outrunning EchoDelay) rather than
        // stalling the newest shot waiting for room.
        private static void EnqueueEcho(Weapon* weapon, FPVector3 position, FPVector3 direction, FP damage)
        {
            var echoes = weapon->PendingEchoes;

            for (int i = 0; i < echoes.Length; i++)
            {
                if (echoes[i].Delay > FP._0)
                    continue;

                echoes[i] = new PendingEcho
                {
                    Delay = weapon->EchoDelay,
                    Position = position,
                    Direction = direction,
                    Damage = damage
                };

                return;
            }
        }

        private static void TickPendingEchoes(Frame f, EntityRef owner, Weapon* weapon, FP deltaTime)
        {
            var echoes = weapon->PendingEchoes;

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
                ProjectileLaunch launch = movement.GetLaunch(echo.Position, pelletDirection);

                if (launch.IsValid == false)
                    continue;

                EntityRef entity = ProjectileSpawner.Spawn(f, owner, weaponData.ProjectileData, launch, echo.Damage, DamageSource.Weapon, element: weaponData.Element);
                ApplyProjectilePerks(f, entity, weapon, false, false);
            }
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
                    RevertEmergencyReload(f, entity, weapon);
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

        // A real (ammo-depleted) reload takes 0 time for whoever equipped
        // OverdriveInstantReloadSkillAction once Rage is maxed out - see IsInstantReloadOverdriven.
        // Treated the same as a weapon authored with ReloadDuration <= 0: instant top-up plus the
        // WeaponReloaded event, not a ReloadTimer that just happens to be very short.
        private static void StartReload(Frame f, EntityRef entity, Weapon* weapon)
        {
            weapon->TimeSinceFireReleased = FP._0;

            ApplyMagazineEmptiedPerks(f, entity, weapon);

            if (weapon->ReloadDuration > FP._0 && IsInstantReloadOverdriven(f, entity) == false)
            {
                weapon->ReloadTimer = StatUtility.GetReloadDuration(f, entity, weapon->ReloadDuration);
                TryApplyEmergencyReload(f, entity, weapon);
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
        private static void ApplyMagazineEmptiedPerks(Frame f, EntityRef entity, Weapon* weapon)
        {
            if (weapon->HasEmptyChamber == true && f.Unsafe.TryGetPointer<Transform3D>(entity, out var transform) == true)
            {
                HitEffectUtility.ApplyShockwave(f, transform->Position, weapon->EmptyChamberRadius, entity, weapon->EmptyChamberKnockback);
            }

            if (weapon->HasCombatReboot == true && f.Unsafe.TryGetPointer<CharacterSkills>(entity, out var skills) == true)
            {
                SkillSystem.ReduceCooldown(f, skills, SkillSlotId.HeroSkill, weapon->CombatRebootCooldownReduction);
            }
        }

        // Only applied for a real (ReloadTimer-driven) reload, not the instant-reload-overdriven
        // case just below (nothing to be mid-reload during) or the idle auto-top-up in
        // UpdateReload (that's not fictionally "reloading" at all). Reverted the moment the real
        // reload actually finishes - see UpdateReload's timer-complete branch.
        private static void TryApplyEmergencyReload(Frame f, EntityRef entity, Weapon* weapon)
        {
            if (weapon->HasEmergencyReload == false || weapon->EmergencyReloadApplied == true)
                return;

            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->MoveSpeedMultiplier += weapon->EmergencyReloadMoveSpeedBonus;
            stats->DamageReduction += weapon->EmergencyReloadDamageReduction;
            weapon->EmergencyReloadApplied = true;
        }

        private static void RevertEmergencyReload(Frame f, EntityRef entity, Weapon* weapon)
        {
            if (weapon->EmergencyReloadApplied == false)
                return;

            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == true)
            {
                stats->MoveSpeedMultiplier -= weapon->EmergencyReloadMoveSpeedBonus;
                stats->DamageReduction -= weapon->EmergencyReloadDamageReduction;
            }

            weapon->EmergencyReloadApplied = false;
        }

        private static bool IsInstantReloadOverdriven(Frame f, EntityRef entity)
        {
            return f.Has<InstantReloadOverdrive>(entity) == true
                && f.Unsafe.TryGetPointer<RageOverdrive>(entity, out var rage) == true
                && rage->Overdriven == true;
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

        private static void FireHitscan(Frame f, EntityRef owner, Weapon* weapon, WeaponDataAsset weaponData,
            FP damage, FPVector3 origin, FPVector3 direction, bool isExplosiveProc, bool isCataclysm)
        {
            FP range = weaponData.Range * weapon->RangeMultiplier;
            int pelletCount = weaponData.PelletCount > 0 ? weaponData.PelletCount : 1;

            for (int i = 0; i < pelletCount; i++)
            {
                FPVector3 pelletDirection = FPQuaternion.Euler(0, GetPelletAngle(i, pelletCount, weaponData.SpreadAngle), 0) * direction;
                Hit3D? hit = f.Physics3D.Raycast(origin, pelletDirection, range, -1, QueryOptions.HitAll);

                bool didHit = hit.HasValue == true && hit.Value.Entity != owner;
                FPVector3 endPoint;

                if (didHit == true)
                {
                    DamageUtility.ApplyDamage(f, hit.Value.Entity, damage, owner, DamageSource.Weapon);
                    Log.Debug($"[Weapon] Hitscan from {owner} hit {hit.Value.Entity} for {damage} base damage");

                    // Hitscan has no Effects list to run through HitEffectUtility (unlike
                    // ProjectileHitData/AreaDamage), so this is called directly here instead.
                    StatusEffectUtility.TryApplyElementalStatus(f, hit.Value.Entity, owner, DamageSource.Weapon,
                        weaponData.Element, damage);

                    // Hit3D.Point only reads real data when the query passes
                    // QueryOptions.ComputeDetailedInfo (see ProjectileSystem.ResolveHitPoint's own
                    // comment on this) - this raycast doesn't, so an entity hit resolves its own
                    // Transform3D instead, falling back to the raycast distance along the ray for the
                    // (rare) non-entity/no-Transform3D case.
                    FPVector3 hitPosition = f.Unsafe.TryGetPointer<Transform3D>(hit.Value.Entity, out var hitTransform)
                        ? hitTransform->Position
                        : origin + pelletDirection * range * hit.Value.CastDistanceNormalized;

                    endPoint = hitPosition;

                    // Only pellet 0 of a volley procs Explosive Sequence/Cataclysm Round - otherwise
                    // an N-pellet shotgun would detonate N explosions off a single trigger pull.
                    // Quantum Rounds stays live on every pellet since it's already a genuine per-hit
                    // effect, not a per-shot one.
                    ApplyHitscanWeaponPerks(f, owner, weapon, hit.Value.Entity, hitPosition, damage,
                        isExplosiveProc && i == 0, isCataclysm && i == 0);
                }
                else
                {
                    Log.Debug($"[Weapon] Hitscan from {owner} missed");
                    endPoint = origin + pelletDirection * range;
                }

                // Hitscan never spawns an entity (unlike Projectile, which the view tracks via
                // ProjectileDestroyed) - this is the only view hook for a hitscan pellet's tracer/
                // impact VFX, see WeaponView/WeaponTracerView.
                f.Events.HitscanFired(owner, origin, endPoint, didHit);
            }
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

        // Quantum Rounds/Explosive Sequence/Cataclysm Round's Hitscan equivalent - a Hitscan weapon
        // has no Projectile entity for these to hook off of (see DirectHitData for the
        // Projectile-type version), so they apply directly here instead, synchronously with the
        // raycast hit itself.
        private static void ApplyHitscanWeaponPerks(Frame f, EntityRef owner, Weapon* weapon, EntityRef hitEntity,
            FPVector3 point, FP damage, bool isExplosiveProc, bool isCataclysm)
        {
            if (weapon->HasQuantumRounds == true
                && WeaponPerkUtility.TryFindNearestEnemy(f, point, weapon->QuantumRoundsRadius, hitEntity, out var nearby) == true)
            {
                DamageUtility.ApplyDamage(f, nearby, damage * weapon->QuantumRoundsDamageMultiplier, owner, DamageSource.Weapon);
            }

            // Bigger Boom (Pixie passive ascension) - read live rather than baked into the weapon at
            // equip time, so picking (or ranking up) Bigger Boom mid-run scales every explosion
            // immediately, and multiple ranks compound off the same unscaled base radius instead of
            // off an already-scaled one. No-op (multiplier 1) for every hero without it - see
            // DamageUtility.ResolvePixieExplosionRadiusMultiplier.
            FP radiusMultiplier = DamageUtility.ResolvePixieExplosionRadiusMultiplier(f, owner);

            if (isCataclysm == true)
            {
                HitEffectUtility.ApplyExplosion(f, point, weapon->CataclysmRadius * radiusMultiplier, owner,
                    damage * weapon->CataclysmDamageMultiplier, DamageSource.Weapon);
            }
            else if (isExplosiveProc == true)
            {
                HitEffectUtility.ApplyExplosion(f, point, weapon->ExplosiveSequenceRadius * radiusMultiplier, owner,
                    damage * weapon->ExplosiveSequenceDamageMultiplier, DamageSource.Weapon);
            }
        }

        private static void FireProjectile(Frame f, EntityRef owner, Weapon* weapon, WeaponDataAsset weaponData,
            FP damage, FPVector3 casterPosition, FP aimAngle, FPVector3 holdOffset, FPVector3 spawnPosition, FPVector3 aimDirection,
            EntityRef target, bool aimAtCenter, bool isExplosiveProc, bool isCataclysm)
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
                    ? movement.GetLaunchToTarget(resolvedOrigin, resolvedOrigin + pelletRotation * delta)
                    : movement.GetLaunch(spawnPosition, pelletRotation * aimDirection);

                if (launch.IsValid == false)
                {
                    Log.Error($"[Weapon] {owner} resolved no valid launch for pellet {i} toward {target} - skipped");
                    continue;
                }

                EntityRef entity = ProjectileSpawner.Spawn(f, owner, weaponData.ProjectileData, launch, damage, DamageSource.Weapon,
                    target: target, element: weaponData.Element);

                // Only pellet 0 of a volley procs Explosive Sequence/Cataclysm Round - see FireHitscan.
                ApplyProjectilePerks(f, entity, weapon, isExplosiveProc && i == 0, isCataclysm && i == 0);

                Log.Debug($"[Weapon] Spawned pellet {i}/{pelletCount} from {owner} with velocity {launch.Velocity}");
            }
        }

        // Bakes this shot's weapon-perk state onto the just-spawned Projectile - Piercing
        // Rounds/Ricochet (RemainingPierces/RemainingBounces), Long Barrel
        // (MaxDistanceMultiplier), and this specific shot's Explosive Sequence/Cataclysm Round
        // flags (see DirectHitData for how the latter two are consumed on impact).
        private static void ApplyProjectilePerks(Frame f, EntityRef entity, Weapon* weapon, bool isExplosiveProc, bool isCataclysm)
        {
            if (f.Unsafe.TryGetPointer<Projectile>(entity, out var projectile) == false)
                return;

            projectile->RemainingPierces += weapon->BonusPierce;
            projectile->RemainingBounces += weapon->BonusBounces;
            projectile->MaxDistanceMultiplier = weapon->RangeMultiplier;
            projectile->IsExplosiveProc = isExplosiveProc;
            projectile->IsCataclysm = isCataclysm;
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
