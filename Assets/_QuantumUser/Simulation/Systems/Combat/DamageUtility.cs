namespace Quantum
{
    using Photon.Deterministic;

    // Additive: the default - stacks with whatever external impulse/velocity the target already
    // has this tick, so multiple simultaneous sources (two players, a player plus an environmental
    // push) combine as expected. Override: replaces the target's existing external
    // impulse/velocity instead, for a knockback source that can itself land several hits in the
    // same tick or in quick succession (e.g. multi-pellet weapons) - without this, each pellet's
    // impulse would stack with the others and launch the target far past what any single pellet
    // was tuned for.
    public enum KnockbackApplyMode { Additive, Override }

    // Shared entry point for dealing damage - Armor reduces the hit flat, Shield absorbs what's
    // left before Health does, so every damage source (hitscan, projectiles, melee/skill attacks)
    // gets identical handling. Crit and elemental procs are rolled here too, which is why they
    // apply to skills and explosions and not just weapon fire.
    //
    // On death, an Enemy is parked in EnemyActionPhase.Dead for EnemyDataAsset.DeathLingerTime (ticked
    // down by EnemySystem) instead of being destroyed immediately, so the view has time to play a
    // die animation; anything else without an Enemy component (e.g. the player) is destroyed right
    // away as before. Filler/Normal-tier enemies (EnemyDataAsset.Tier) skip that lingering path
    // entirely - they're destroyed immediately and fire EnemyExploded instead, replacing the die
    // animation with an explosion (see FireEnemyExploded below). Only Specialist/Elite/Boss linger.
    public static unsafe class DamageUtility
    {
        // source defaults to None so anything without a source-specific multiplier to claim (enemy
        // attacks, environment) reads unchanged at the call site.
        // bypassOutgoingResolution skips ResolveOutgoingDamage entirely (no crit reroll, no owner
        // stat multiplier) - used by StatusEffectSystem so a DoT tick applies the exact magnitude
        // rolled once when the status was applied, instead of re-rolling crit every tick.
        // silent flags the fired EntityDamaged event so HitFeedback skips its flash - see
        // SentryDecaySystem, the only caller that passes true today.
        // isChainedExplosion/isExplosion both feed TryMarkExplodeOnDeath (Pixie's Chain Reaction
        // passive) - see that method's own comment. Both default false, so every existing caller is
        // unaffected.
        // hitIndex only matters to EntityDamaged's own dedup-avoidance (see Events.qtn's comment on
        // EntityDamaged.HitIndex) - defaults to 0 for every caller except WeaponSystem.FireHitscan's
        // pellet loop, the only place multiple identical-damage hits can land on one stationary
        // target within a single tick.
        public static void ApplyDamage(Frame f, EntityRef target, FP damage, EntityRef owner,
            DamageSource source = DamageSource.None, bool bypassOutgoingResolution = false,
            ElementType element = ElementType.Neutral, bool silent = false,
            bool isChainedExplosion = false, bool isExplosion = false,
            byte hitIndex = 0)
        {
            if (f.Unsafe.TryGetPointer<Health>(target, out var health) == false)
            {
                Log.Debug($"[Damage] {target} has no Health component - hit ignored");
                return;
            }

            // An unseeded Health sits at 0 and reads as already-dead here, which silently makes the
            // entity immune to every hit - worth saying out loud while MaxHealth is 0.
            if (health->CurrentHealth <= FP._0)
            {
                Log.Debug($"[Damage] {target} ignored a hit - CurrentHealth is {health->CurrentHealth} " +
                          $"(MaxHealth {health->MaxHealth}){(health->MaxHealth <= FP._0 ? " - never seeded, so it can't be damaged" : " - already dead")}");
                return;
            }

            // Gates damage only, not ApplyKnockback below - invulnerability is damage-immunity,
            // not knockback-immunity.
            if (f.Has<Invulnerable>(target))
            {
                Log.Debug($"[Damage] {target} is Invulnerable - hit ignored");
                return;
            }

            // A landed weapon hit, not a DoT tick replaying an already-resolved magnitude (see
            // bypassOutgoingResolution below) - StatusEffectSystem's Burn/Poison ticks are also
            // tagged DamageSource.Weapon when they trace back to a weapon's elemental proc, so
            // bypassOutgoingResolution is what actually distinguishes a real trigger pull from that.
            if (source == DamageSource.Weapon && bypassOutgoingResolution == false)
            {
                RageOverdriveUtility.TryAdvanceStack(f, owner);
                f.Signals.OnWeaponHitLanded(target, owner);
            }

            FP totalDamage;
            bool isCritical;

            if (bypassOutgoingResolution == true)
            {
                totalDamage = damage;
                isCritical = false;
            }
            else
            {
                totalDamage = ResolveOutgoingDamage(f, owner, target, damage, source, out isCritical);
            }

            if (isCritical == true)
            {
                f.Signals.OnCriticalHit(target, owner, totalDamage, source);

                // Pixie's Volatile Payload - a narrower sibling fire, not a replacement, so every
                // existing OnCriticalHit subscriber (Flashpoint, weapon perks, Rift Mutations) is
                // unaffected. See Combat.qtn's own comment on why this isn't just an extra parameter
                // on OnCriticalHit itself.
                if (isExplosion == true)
                {
                    f.Signals.OnExplosionCriticalHit(target, owner, totalDamage, source);
                }
            }

            // Rupture - target-side vulnerability, applied once here so every damage source (hitscan,
            // projectile, melee, DoT ticks) respects it identically. Multiplies the attacker's
            // already-resolved damage rather than replacing Armor/Shield mitigation below it.
            totalDamage *= StatusEffectUtility.GetIncomingDamageMultiplier(f, target);
            totalDamage *= ResolveDamageReduction(f, target);

            FP frontalMultiplier = ResolveFrontalDamageMultiplier(f, target, owner);
            totalDamage *= frontalMultiplier;

            FP mitigatedDamage = ReduceByArmor(f, target, totalDamage);
            FP remaining = AbsorbWithShield(f, target, mitigatedDamage);
            FP shieldAbsorbed = mitigatedDamage - remaining;
            bool directHit = bypassOutgoingResolution == false;

            // Health/Shield damage reporting - generic, fired for every source including None
            // (environmental), unlike OnWeaponHitLanded above which is weapon-only. See Combat.qtn's
            // own comment; consumed today by Max's Vendetta (docs/max-vendetta-fire-mastery.md).
            if (shieldAbsorbed > FP._0)
            {
                f.Signals.OnShieldDamageApplied(target, owner, shieldAbsorbed, source, directHit);
            }

            // Rift Mutation mark-application content (Heavy/Close/Long/Execution/First Contact/Skill/
            // Critical Fracture) - evaluated here, not via a signal, so pre-damage health/distance are
            // both still live and every mechanic shares one deterministic priority order (see
            // docs/rift-mutations.md's "Event resolution order"). Same bypassOutgoingResolution gate
            // OnWeaponHitLanded above already uses to exclude DoT-tick replays.
            if (bypassOutgoingResolution == false)
            {
                RiftMutationMarkUtility.EvaluateOnDamage(f, target, owner, source, remaining,
                    health->CurrentHealth, health->MaxHealth, isCritical);
                RiftMutationMarkUtility.EvaluateLastStand(f, target, remaining);
            }

            FP healthAfter = health->CurrentHealth - remaining;

            // Too Angry to Die - a hit that would otherwise be lethal instead leaves the owner at 1
            // Health and force-ends their current Overdrive activation (see CheatDeathUtility). Only
            // ever intervenes on an actually-lethal hit, never a survivable one.
            if (healthAfter <= FP._0 && CheatDeathUtility.TryPreventLethal(f, target) == true)
            {
                healthAfter = FP._1;
            }

            health->CurrentHealth = FPMath.Max(FP._0, healthAfter);

            if (remaining > FP._0)
            {
                f.Signals.OnHealthDamageApplied(target, owner, remaining, source, directHit);
            }

            // Read now, not by the view re-resolving Target's Transform3D later - a killing blow can
            // destroy Target before this tick is even done (see the death branch below), which would
            // otherwise silently drop the hit's floating damage number.
            FPVector3 hitPosition = f.Unsafe.TryGetPointer<Transform3D>(target, out var targetTransform)
                ? targetTransform->Position
                : FPVector3.Zero;

            f.Events.EntityDamaged(target, owner, totalDamage, isCritical, element, silent, frontalMultiplier < FP._1, hitPosition, hitIndex);

            // Zara's Resonance - builds from dealing damage only, scaled by the amount actually
            // dealt. No-ops on anything without Resonance (every hero but Zara).
            ResonanceUtility.OnDamageDealt(f, owner, totalDamage);

            Log.Debug($"[Damage] {target} took {remaining} to health (raw {damage}, after stats {totalDamage}) " +
                      $"-> {health->CurrentHealth}/{health->MaxHealth}");

            // A landed hit, not just a killing one - an enemy that survives this shot can still go
            // on to die from something else entirely and explode then, same as one that dies right
            // here.
            TryMarkExplodeOnDeath(f, owner, target, isChainedExplosion, isExplosion);

            if (health->CurrentHealth <= FP._0)
            {
                f.Events.EntityDied(target, owner);
                f.Signals.OnEntityKilled(target, owner, source);
                ExperienceUtility.TrySpawnDrop(f, target, owner);
                ScrapUtility.TrySpawnDrop(f, target, owner);
                RiftShardUtility.TrySpawnDrop(f, target, owner);
                CoinUtility.TrySpawnDrop(f, target, owner);

                if (f.Unsafe.TryGetPointer<Enemy>(target, out var enemy) == true)
                {
                    EnemyDataAsset data = f.FindAsset(enemy->EnemyData);

                    if (data.Tier == EnemyTier.Filler || data.Tier == EnemyTier.Normal)
                    {
                        FireEnemyExploded(f, target, enemy->EnemyData);
                        TryExplodeOnDeath(f, owner, target, EnemyMovementUtility.ResolveEntityRadius(f, target), health->MaxHealth, enemy->EnemyData);
                        f.Destroy(target);
                    }
                    else
                    {
                        enemy->Phase = EnemyActionPhase.Dead;
                        enemy->StateTimer = data.DeathLingerTime;

                        f.Signals.OnEnemyDied(target);
                        TryExplodeOnDeath(f, owner, target, EnemyMovementUtility.ResolveEntityRadius(f, target), health->MaxHealth, enemy->EnemyData);
                    }
                }
                else if (f.Has<PlayerLink>(target) == true)
                {
                    RespawnPlayer(f, target, health);
                }
                else
                {
                    TrySentryOverload(f, owner, target);

                    // ExplodeOnDestroy (see ExplodeOnDestroy.qtn) - the damage-death counterpart to
                    // DestroyAfterTimeSystem's own trigger, so a damageable Mini Bomb (Health seeded
                    // to 1, a Decoy tag drawing enemy aggro, a real trap) detonates the instant an
                    // enemy actually kills it, not just when its fuse times out. No-op for anything
                    // without the component, same as every other optional check in this branch.
                    ExplodeOnDestroyUtility.TryDetonate(f, target);

                    f.Destroy(target);
                }
            }
        }

        // Unstable Mixture (Pixie ascension) - the single resolution point for her explosion radius
        // multiplier, reused by every one of her own explosion sources (weapon explosive procs via
        // WeaponSystem.Equip, her bomb skill via AreaHitData.Detonate, Backblast's dropped bombs) -
        // not just the death-explosion mechanic below (TryExplodeOnDeath), which read the raw field
        // directly before this existed. Gated on RequiresExplosion (true only for
        // Pixie's own grant, see ChainReactionPassiveData.Apply) rather than merely "has
        // MarkExplosiveDeath" - Max's Berserk grant carries the same component but was never seeded
        // by Pixie's own passive, so gating on presence alone would read whatever value happens to
        // sit in BonusRadiusMultiplier for him instead of a guaranteed no-op.
        public static FP ResolvePixieExplosionRadiusMultiplier(Frame f, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<MarkExplosiveDeath>(owner, out var mark) == false || mark->RequiresExplosion == false)
                return FP._1;

            return mark->BonusRadiusMultiplier;
        }

        // Enemies only - a hit on the player or a non-Enemy prop has nothing here to mark. Owner is
        // whoever landed THIS hit (a bullet, a blast, a DoT tick), not necessarily who ends up
        // finishing the target off later - MarkExplosiveDeath is granted by an upgrade shared across
        // heroes (Skills/MarkExplosiveDeathSkillAction), so it doesn't matter whether it was Max's
        // gun or Pixie's bomb that landed this particular hit.
        // isChainedExplosion is always false except when called from inside this same class's own
        // TryExplodeOnDeath (a death-explosion chaining into another mark) - see MarkExplosiveDeath.qtn
        // for what that ascension does with it. Backblast's dropped bombs (see ExplodeOnDestroyUtility)
        // are real AreaHitData detonations, so they already reach this via the normal isExplosion path
        // below like any other Pixie explosion - a guaranteed/bypass mark for Backblast rank 3 is
        // instead handled entirely separately, by ForceMarkOnDetonate (see ExplodeOnDestroyUtility),
        // not through this generic per-hit gate at all.
        private static void TryMarkExplodeOnDeath(Frame f, EntityRef owner, EntityRef target, bool isChainedExplosion, bool isExplosion)
        {
            if (f.Unsafe.TryGetPointer<Enemy>(target, out var enemy) == false)
                return;

            if (f.Unsafe.TryGetPointer<MarkExplosiveDeath>(owner, out var mark) == false || mark->Stacks == 0)
                return;

            // Pixie's Chain Reaction only marks off an actual explosion (weapon explosive procs -
            // Cataclysm Round/Explosive Sequence - or her own bomb skill/cluster bomblets/dash bombs),
            // never a plain non-explosive hit. RequiresExplosion defaults false, so Max's Berserk (the
            // only other grantor) is unaffected by any of this and still marks on any hit/source
            // exactly as before.
            if (mark->RequiresExplosion == true && isExplosion == false && isChainedExplosion == false)
            {
                return;
            }

            FP durationMultiplier = FP._1;

            if (isChainedExplosion == true)
            {
                // Chain Reaction (Pixie ascension) - a chained/secondary blast only re-marks anyone
                // it also catches once this has been taken (0 is the base passive's default, off),
                // and then at reduced effectiveness - a direct hit always marks at full duration
                // regardless of this ascension.
                if (mark->ChainReactionMultiplier <= FP._0)
                    return;

                durationMultiplier = mark->ChainReactionMultiplier;
            }

            if (mark->HasTierGate == true)
            {
                EnemyDataAsset data = f.FindAsset(enemy->EnemyData);

                if ((byte)data.Tier > mark->MaxAffectedTier)
                    return;
            }

            // Chain Reaction proc chance - not every qualifying explosion hit marks (see
            // MarkExplosiveDeath.MarkChance). Gated behind HasMarkChance so Max's Berserk grant, which
            // never sets it, still marks unconditionally. Rolled last, after every other gate, so a hit
            // that was never going to mark (wrong tier/source) doesn't burn an RNG roll.
            if (mark->HasMarkChance == true && RollChance(f, mark->MarkChance) == false)
                return;

            ExplodeOnDeathConfig config = f.FindAsset(f.RuntimeConfig.ExplodeOnDeathConfig);

            f.AddOrGet<ExplodeOnDeath>(target, out var explode);
            explode->Remaining = config.Duration * durationMultiplier;
        }

        // Filler-tier death replacement for the lingering die animation - fires the explosion event
        // with the dying enemy's REAL collider radius (EnemyMovementUtility.ResolveEntityRadius).
        // Source travels with the event so EffectsManager can resolve this enemy type's own
        // ExplosionColor (EnemyDataAsset.View.cs) without this needing to know anything about VFX.
        private static void FireEnemyExploded(Frame f, EntityRef target, AssetRef<EnemyDataAsset> dataRef)
        {
            if (f.Unsafe.TryGetPointer<Transform3D>(target, out var transform) == false)
                return;

            f.Events.EnemyExploded(target, transform->Position, EnemyMovementUtility.ResolveEntityRadius(f, target), dataRef);
        }

        // Enemies only (see the call site) - gated purely on the dying target's own ExplodeOnDeath
        // tag (see TryMarkExplodeOnDeath) - there's no owner-side "my kills explode" concept anymore,
        // just "this specific enemy was marked at some point and it just died." DamagePercent is read
        // from the shared RuntimeConfig.ExplodeOnDeathConfig rather than anything per-upgrade, so
        // every source of the mark hits identically. Radius comes from the dying enemy's own REAL
        // collider radius (EnemyMovementUtility.ResolveEntityRadius) - same reasoning FireEnemyExploded
        // already uses it for - so a Brute still explodes bigger than a Grunt with zero extra tuning
        // per enemy type, just off whatever collider each actually has; maxHealth is passed in the
        // same way, off the Health component that just hit zero. enemyData just travels through to
        // the event so EffectsManager can tint the shared blast prefab with this enemy type's own
        // ExplosionColor instead of always playing it flat white.
        private static void TryExplodeOnDeath(Frame f, EntityRef owner, EntityRef target, FP radius, FP maxHealth, AssetRef<EnemyDataAsset> enemyData)
        {
            if (f.Has<ExplodeOnDeath>(target) == false)
                return;

            if (radius <= FP._0)
            {
                Log.Debug($"[Damage] {target} has no collider radius to explode with - its ExplodeOnDeath mark was skipped");
                return;
            }

            if (f.Unsafe.TryGetPointer<Transform3D>(target, out var transform) == false)
                return;

            ExplodeOnDeathConfig config = f.FindAsset(f.RuntimeConfig.ExplodeOnDeathConfig);
            FP blastRadius = radius * config.RadiusMultiplier;
            FP damage = maxHealth * config.DamagePercent;

            // Rift-Marked kills detonate bigger and harder - see docs/elemental-reactions.md for
            // what applies a Rift Mark; this stacks with every bonus below, it's not a replacement
            // for any of them. Captured once here (target is about to be destroyed, so this is the
            // last point IsRiftMarked can still be read) and carried on the event so the view can
            // play a visually distinct blast for it - see ExplodeOnDeathDetonated's own comment.
            bool isRiftMarked = StatusEffectUtility.IsRiftMarked(f, target);

            if (isRiftMarked == true)
            {
                blastRadius *= config.RiftMarkRadiusMultiplier;
                damage *= config.RiftMarkDamageMultiplier;
            }

            // Unstable Mixture (Pixie ascension) applies unconditionally - BonusRadiusMultiplier/
            // BonusDamageMultiplier both default to 1, so Max's Berserk is unaffected. TierRadiusMultiplier
            // only for a Specialist+ kill, RADIUS ONLY (not damage - see MarkExplosiveDeath.qtn's own
            // comment on why), on top of whatever that tougher enemy's own naturally bigger radius/
            // health already contributed.
            if (f.Unsafe.TryGetPointer<MarkExplosiveDeath>(owner, out var mark) == true)
            {
                blastRadius *= mark->BonusRadiusMultiplier;
                damage *= mark->BonusDamageMultiplier;

                EnemyDataAsset data = f.FindAsset(enemyData);

                if (data.Tier >= EnemyTier.Specialist)
                {
                    blastRadius *= mark->TierRadiusMultiplier;
                }
            }

            if (damage <= FP._0)
                return;

            // Flags this blast's own hits as chained, so TryMarkExplodeOnDeath only re-marks anyone
            // it also catches if Chain Reaction has been taken (see that method's own comment).
            HitEffectUtility.ApplyDamageInRadius(f, transform->Position, blastRadius, owner, damage, DamageSource.Skill, config.TargetMask, isChainedExplosion: true);
            f.Events.ExplodeOnDeathDetonated(owner, transform->Position, blastRadius, enemyData, isRiftMarked);

            Log.Debug($"[Damage] {target}'s marked death exploded at {transform->Position} radius {blastRadius} for {damage}");
        }

        // Fully independent from the enemy MarkExplosiveDeath/ExplodeOnDeath kill-chain mechanic above
        // - its own Radius/Damage (SentryOverloadUpgrade, granted by SentryAddOverloadSkillAction and
        // copied onto the spawned sentry by SpawnSentrySkillAction.ApplyOverloadUpgrade), no shared
        // RuntimeConfig. Still uses the same low-level HitEffectUtility.ApplyDamageInRadius every AoE
        // in the game already uses, just with its own values instead of ExplodeOnDeathConfig's.
        private static void TrySentryOverload(Frame f, EntityRef owner, EntityRef target)
        {
            if (f.Unsafe.TryGetPointer<SentryOverloadUpgrade>(target, out var overload) == false)
                return;

            if (overload->Radius <= FP._0 || overload->Damage <= FP._0)
                return;

            if (f.Unsafe.TryGetPointer<Transform3D>(target, out var transform) == false)
                return;

            HitEffectUtility.ApplyDamageInRadius(f, transform->Position, overload->Radius, owner, overload->Damage, DamageSource.Skill, DamageTargetMask.Enemies);
            f.Events.SentryOverloadDetonated(owner, transform->Position, overload->Radius, overload->Source);

            Log.Debug($"[Damage] {target}'s Overload detonated at {transform->Position} radius {overload->Radius} for {overload->Damage}");
        }

        // A lethal hit on a player no longer destroys the entity (the original behavior, shared
        // with Sentry/Mini Bomb/Decoy via the else branch above) - CharacterStats/Weapon/
        // CharacterSkills/LevelUpChoice/GlobalUpgradePicks/RiftMutationPicks/UpgradeHistory all
        // ride on that same entity, so destroying it silently wiped every upgrade earned this run.
        // Instead: refill Health/Shield and teleport back to the run's start position, same
        // "reposition without destroying" idiom PlayerFallSystem/PlayerSpawnUtility already use.
        // Level/TotalExperience/TotalCoins/RiftShards are untouched either way since they already
        // live on Frame.Global, not the player entity.
        private static void RespawnPlayer(Frame f, EntityRef target, Health* health)
        {
            health->CurrentHealth = health->MaxHealth;

            if (f.Unsafe.TryGetPointer<Shield>(target, out var shield) == true)
            {
                shield->Current = shield->Max;
                shield->RechargeTimer = FP._0;
            }

            FPVector3 respawnPosition = f.Global->PlayerSpawnPosition;

            if (f.Unsafe.TryGetPointer<KCC>(target, out var kcc) == true)
            {
                kcc->Teleport(f, respawnPosition);

                // Teleport() only repositions - any knockback/dash momentum from the killing
                // blow would otherwise carry straight into the fresh respawn position.
                kcc->SetKinematicVelocity(FPVector3.Zero);
                kcc->SetDynamicVelocity(FPVector3.Zero);
                kcc->SetExternalImpulse(FPVector3.Zero);
            }
            else if (f.Unsafe.TryGetPointer<Transform3D>(target, out var transform) == true)
            {
                transform->Position = respawnPosition;
            }

            // Move speed is otherwise left carrying whatever it had the instant before death - a
            // player who died mid-Ice/Root/Stun respawned still slowed/pinned/frozen until those
            // timers happened to run out on their own. CharacterStats.MoveSpeedMultiplier is
            // untouched - that's the permanent hero/upgrade baseline, not a status effect.
            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var statusEffects) == true)
            {
                statusEffects->IceRemaining = FP._0;
                statusEffects->StunRemaining = FP._0;
                statusEffects->RootRemaining = FP._0;
            }

            Log.Debug($"[Damage] {target} died and respawned at {respawnPosition}");

            f.Events.PlayerRespawned(target, respawnPosition);
        }

        // CharacterStats.DamageReduction was already seeded from CharacterData (see
        // CharacterSystem.cs) but never actually read anywhere - this is that missing wire-up. A
        // fraction (0 = no reduction, 1 = fully immune), same convention as every other
        // Multiplier-suffixed stat despite the name; clamped so a stacked bonus past 1 can't flip
        // into negative (healing) damage. Target-side, same as Rupture - stacks with it rather than
        // replacing it.
        //
        // Public so DamageReductionUiWidget (View) can read the exact same combined multiplier
        // instead of re-deriving it from CharacterStats/StatusEffects separately - a hand-duplicated
        // copy of this math would silently drift the instant a third source gets added here.
        public static FP ResolveDamageReduction(Frame f, EntityRef target)
        {
            FP multiplier = FP._1;

            if (f.Unsafe.TryGetPointer<CharacterStats>(target, out var stats) == true)
            {
                multiplier *= FPMath.Clamp(FP._1 - stats->DamageReduction, FP._0, FP._1);
            }

            // Max's Too Angry to Die - a timed buff, independent of (and stacking with) the permanent
            // CharacterStats fraction above.
            multiplier *= StatusEffectUtility.GetDamageReductionMultiplier(f, target);

            // Brute's Guardian ascension (Protector Aura, ally-targeted) - its own dedicated pair,
            // not the generic one above, so it can never collide with Too Angry to Die - see
            // StatusEffects.qtn's own comment on GuardianDamageReductionRemaining/Amount.
            multiplier *= StatusEffectUtility.GetGuardianDamageReductionMultiplier(f, target);

            // A third, independent timed DR - Brute's Guardian rank 3 reactive proc and Bodyguard
            // rank 3's own proc both write here, layered on top of Guardian's continuous aura DR
            // above rather than replacing it - see StatusEffects.qtn's own comment on
            // TemporaryDamageReductionRemaining/Amount.
            multiplier *= StatusEffectUtility.GetTemporaryDamageReductionMultiplier(f, target);

            return multiplier;
        }

        // Target-side, stacks with ResolveDamageReduction above rather than replacing it - an enemy
        // with EnemyTrait.FrontalDamageReduction takes less damage from a hit landing within its
        // current facing arc (Aim.Angle), full damage from anywhere else. Uses the attacker's own
        // position (owner), not a projectile's travel direction at impact - the shield blocks based
        // on where the hit came from relative to the defender, not the angle it happened to arrive at.
        private static FP ResolveFrontalDamageMultiplier(Frame f, EntityRef target, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<Enemy>(target, out var enemy) == false)
                return FP._1;

            EnemyDataAsset enemyData = f.FindAsset(enemy->EnemyData);

            if (enemyData.Stats.HasTrait(EnemyTrait.FrontalDamageReduction) == false)
                return FP._1;

            if (f.Unsafe.TryGetPointer<Aim>(target, out var aim) == false
                || f.Unsafe.TryGetPointer<Transform3D>(target, out var targetTransform) == false
                || f.Unsafe.TryGetPointer<Transform3D>(owner, out var ownerTransform) == false)
                return FP._1;

            FPVector3 toAttacker = ownerTransform->Position - targetTransform->Position;
            FPVector3 toAttackerFlat = new FPVector3(toAttacker.X, FP._0, toAttacker.Z);

            if (toAttackerFlat.SqrMagnitude <= FP._0)
                return FP._1; // attacker directly overhead/underfoot - no horizontal direction to judge front from

            FPVector3 facing = FPQuaternion.Euler(FP._0, aim->Angle, FP._0) * FPVector3.Forward;
            FP dot = FPVector3.Dot(facing, toAttackerFlat.Normalized);
            FP arcCos = FPMath.Cos(enemyData.Stats.FrontalDamageReductionArcDegrees * FP._0_50 * FP.Deg2Rad);

            if (dot < arcCos)
                return FP._1; // outside the frontal arc

            return FPMath.Clamp(FP._1 - enemyData.Stats.FrontalDamageReductionAmount, FP._0, FP._1);
        }

        // Attacker-side, unlike ResolveFrontalDamageMultiplier above (target-side) - Close
        // Quarters/Longshot (Global Upgrades) lerp between the attacker's own
        // NearDamageMultiplier/FarDamageMultiplier off the flat attacker-target distance at the
        // moment the hit resolves. Fixed design-constant thresholds for now (not per-asset tunable)
        // - a placeholder starting point for balance passes, same convention the asset generators
        // already use for their own untuned proc magnitudes.
        private static readonly FP RangeDamageNearThreshold = 5;
        private static readonly FP RangeDamageFarThreshold = 12;

        private static FP ResolveRangeDamageMultiplier(Frame f, EntityRef owner, EntityRef target, CharacterStats* stats)
        {
            if (stats->NearDamageMultiplier == FP._1 && stats->FarDamageMultiplier == FP._1)
                return FP._1; // no Close Quarters/Longshot picked - skip the Transform3D lookups entirely

            if (f.Unsafe.TryGetPointer<Transform3D>(owner, out var ownerTransform) == false
                || f.Unsafe.TryGetPointer<Transform3D>(target, out var targetTransform) == false)
                return FP._1;

            FP distance = FPVector3.Distance(ownerTransform->Position, targetTransform->Position);

            if (distance <= RangeDamageNearThreshold)
                return stats->NearDamageMultiplier;

            if (distance >= RangeDamageFarThreshold)
                return stats->FarDamageMultiplier;

            FP t = (distance - RangeDamageNearThreshold) / (RangeDamageFarThreshold - RangeDamageNearThreshold);
            return FPMath.Lerp(stats->NearDamageMultiplier, stats->FarDamageMultiplier, t);
        }

        private static FP ReduceByArmor(Frame f, EntityRef target, FP damage)
        {
            if (f.Unsafe.TryGetPointer<Armor>(target, out var armor) == false)
                return FPMath.Max(FP._0, damage);

            return FPMath.Max(FP._0, damage - armor->Amount);
        }

        // Returns what's left for Health after the shield soaks what it can. Entities without a
        // Shield component just pass the hit straight through.
        private static FP AbsorbWithShield(Frame f, EntityRef target, FP damage)
        {
            if (f.Unsafe.TryGetPointer<Shield>(target, out var shield) == false)
                return damage;

            shield->RechargeTimer = shield->RechargeDelay;

            if (shield->Current <= FP._0)
            {
                Log.Debug($"[Damage] {target} shield is empty ({shield->Current}/{shield->Max}) - {damage} passes to health, " +
                          $"recharge held off for {shield->RechargeDelay}s at {shield->RechargeRate}/s");
                return damage;
            }

            FP absorbed = FPMath.Min(shield->Current, damage);
            shield->Current -= absorbed;

            Log.Debug($"[Damage] {target} shield absorbed {absorbed} of {damage} -> {shield->Current}/{shield->Max}, " +
                      $"recharge held off for {shield->RechargeDelay}s at {shield->RechargeRate}/s");

            // Current was > 0 to reach here (see the early-return above) - this is the exact tick
            // it broke, not a hit landing while already broken.
            if (shield->Current <= FP._0)
            {
                f.Signals.OnShieldBroken(target);
                f.Events.ShieldBroken(target);
            }

            return damage - absorbed;
        }

        // The attacker's own stats plus whatever weapon it holds. Read from the owner at impact
        // rather than captured when a shot was fired, so a projectile crits by the weapon its
        // shooter holds when it lands. An attacker without CharacterStats deals its damage flat -
        // no multiplier, and no crit, since there'd be nothing to multiply by.
        private static FP ResolveOutgoingDamage(Frame f, EntityRef owner, EntityRef target, FP damage, DamageSource source,
            out bool isCritical)
        {
            isCritical = false;

            // Intimidate (Brute's Protector Aura) - reduces the ATTACKER's own outgoing damage,
            // checked before the CharacterStats gate below since this has to affect enemies too, and
            // enemies never carry CharacterStats. Fearless (Brute's own ascension) is the mirror
            // case - a bonus Brute himself deals against an Intimidated target - so it also has to
            // sit outside that gate rather than depend on it.
            damage *= StatusEffectUtility.GetOutgoingDamageMultiplier(f, owner);
            damage *= ProtectorAuraUtility.GetFearlessBonusMultiplier(f, owner, target);

            if (f.Unsafe.TryGetPointer<CharacterStats>(owner, out var stats) == false)
                return damage;

            damage *= stats->DamageMultiplier * GetSourceMultiplier(stats, source);
            damage *= ResolveRangeDamageMultiplier(f, owner, target, stats);

            // Max's Last Stand rank 2 / Run & Gun rank 2 - a temporary Weapon Damage buff, read live
            // rather than baked into CharacterStats.WeaponDamageMultiplier. Scoped to DamageSource.
            // Weapon, same convention GetSourceMultiplier uses.
            if (source == DamageSource.Weapon)
            {
                damage *= StatusEffectUtility.GetTemporaryWeaponDamageMultiplier(f, owner);
            }

            // Pixie's Unstable Targeting - bonus damage against a target currently marked
            // ExplodeOnDeath ("Unstable"), read live rather than baked into CharacterStats.
            // DamageMultiplier, same idiom Hot Target uses below for its own conditional bonus.
            // Gated on MarkExplosiveDeath's presence purely as "does this owner hold the upgrade" -
            // the mark being read is the TARGET's own, regardless of who applied it.
            if (f.Unsafe.TryGetPointer<MarkExplosiveDeath>(owner, out var mark) == true
                && f.Has<ExplodeOnDeath>(target) == true)
            {
                damage *= mark->DamageBonusVsUnstable;
            }

            // Brute's Concussive Impact rank 3 - bonus damage against a currently-Stunned target,
            // read live rather than baked into CharacterStats.DamageMultiplier, same idiom as
            // Unstable Targeting above. (Renamed from the old standalone Crushing Blow ascension's
            // own CrushingBlowUpgrade - see StunDamageBonusUpgrade's own comment.)
            if (f.Unsafe.TryGetPointer<StunDamageBonusUpgrade>(owner, out var stunDamageBonus) == true
                && StatusEffectUtility.IsStunned(f, target) == true)
            {
                damage *= FP._1 + stunDamageBonus->DamageMultiplierBonus;
            }

            // Kai's First Strike - bonus damage on a target's first-ever hit from an owner holding this
            // upgrade, or (rank 3 "Perfect Opening" only, RefreshWindow > 0) once more after the mark
            // has sat untouched for RefreshWindow seconds (see FirstStrikeMarkTimeoutSystem). Gated on
            // the OWNER holding the trait (not "has anyone ever hit this enemy") so another hero's shot
            // landing first in co-op doesn't silently deny Kai his own bonus. FirstStrikeMark also
            // tracks WHICH Kai currently holds the mark (RevengeMark-shaped, see Heroes/Kai/
            // FirstStrike.qtn) - a second Kai's hit reclaims it rather than tracking both at once.
            if (f.Unsafe.TryGetPointer<FirstStrikeUpgrade>(owner, out var firstStrike) == true)
            {
                bool firstStrikeEligible = f.Unsafe.TryGetPointer<FirstStrikeMark>(target, out var firstStrikeMark) == false
                    || firstStrikeMark->MarkedBy != owner;

                if (firstStrikeEligible == true)
                {
                    damage *= FP._1 + firstStrike->DamageMultiplierBonus;
                }

                f.AddOrGet<FirstStrikeMark>(target, out var liveFirstStrikeMark);
                liveFirstStrikeMark->MarkedBy = owner;
                liveFirstStrikeMark->RemainingGrace = firstStrike->RefreshWindow;
            }

            // Kai's Undertow rank 3 "Gravitational Bond" - bonus damage against any Bound enemy (see
            // StatusEffectUtility.ApplyBound/IsBound) - not per-source, since Bound is a status on the
            // enemy like Stun/Intimidate, not an attribution-scoped mark.
            if (f.Unsafe.TryGetPointer<UndertowUpgrade>(owner, out var undertow) == true && undertow->BoundDamageBonus > FP._0
                && StatusEffectUtility.IsBound(f, target) == true)
            {
                damage *= FP._1 + undertow->BoundDamageBonus;
            }

            // Max's Vendetta - bonus damage against whichever enemy currently carries this owner's
            // own RevengeMark, read live rather than baked into CharacterStats.DamageMultiplier, same
            // idiom as Unstable Targeting/Stun Damage Bonus above. No DamageSource restriction, so
            // this applies to weapon/skill/dash/Burn-tick damage alike - a Burn tick's own owner
            // (StatusEffects.BurnOwner) flows through this exact same resolution.
            if (f.Unsafe.TryGetPointer<RevengeMark>(target, out var revengeMark) == true && revengeMark->MarkedBy == owner
                && f.Unsafe.TryGetPointer<RevengeConfig>(owner, out var revengeConfig) == true)
            {
                damage *= FP._1 + revengeConfig->DamageBonus;
            }

            FP chance = stats->CriticalChance;
            FP multiplier = stats->CriticalDamageMultiplier;

            // Weapon-only, same as GetSourceMultiplier above - a weapon's own crit bonus is a
            // property of that weapon, not the character, so a Skill hit (a thrown bomb, a firework)
            // rolls off CharacterStats alone rather than inheriting whatever gun happens to be
            // equipped.
            if (source == DamageSource.Weapon && f.Unsafe.TryGetPointer<Weapon>(owner, out var weapon) == true)
            {
                chance += weapon->CriticalChance;
                multiplier += weapon->CriticalDamageBonus;
            }

            // Max's Hot Target (Fire Mastery) - bonus Critical Chance vs a currently-Burning
            // target, read live rather than baked into CharacterStats.CriticalChance. See
            // docs/max-vendetta-fire-mastery.md.
            if (f.Unsafe.TryGetPointer<ConditionalCriticalModifier>(owner, out var critMod) == true
                && StatusEffectUtility.IsBurning(f, target) == true)
            {
                chance += critMod->CriticalChanceBonusVsBurning;
            }

            if (RollChance(f, chance) == false)
                return damage;

            isCritical = true;
            Log.Debug($"[Damage] {owner} crit for x{multiplier}");

            return damage * multiplier;
        }

        // Stacks on top of DamageMultiplier rather than replacing it, so a build that raises both
        // its global and its weapon damage gets both.
        private static FP GetSourceMultiplier(CharacterStats* stats, DamageSource source)
        {
            switch (source)
            {
                case DamageSource.Weapon: return stats->WeaponDamageMultiplier;
                case DamageSource.Skill: return stats->SkillDamageMultiplier;
                default: return FP._1;
            }
        }

        // Public so StatusEffectUtility.TryApplyElementalStatus can reuse the exact same roll for
        // ElementalChance instead of duplicating this logic.
        public static bool RollChance(Frame f, FP chance)
        {
            if (chance <= FP._0)
                return false;

            int threshold = FPMath.RoundToInt(FPMath.Clamp(chance, FP._0, FP._1) * 10000);

            return f.RNG->Next(0, 10000) < threshold;
        }

        // Shoves whatever movement component the target actually has - KCC (the player) and/or
        // PhysicsBody3D (enemies, loose physics props). Both are applied when an entity somehow has
        // both, rather than one winning, so neither path silently swallows the hit.
        //
        // The KCC path uses KCC.AddExternalImpulse - a velocity impulse the KCC addon folds into
        // DynamicVelocity on its next update
        // (Assets/Photon/QuantumAddons/KCC/Simulation/Processors/EnvironmentProcessor.cs), the
        // same mechanism the existing AutoJumpSystem uses for jump impulses.
        //
        // The upwardForce component alongside the horizontal push isn't just feel, it's
        // load-bearing: MovementDataAsset.asset has DynamicGroundFriction=20 vs.
        // DynamicAirFriction=1 (confirmed by reading the actual tuned values) - ground friction
        // in EnvironmentProcessor.SetDynamicVelocity is proportional to current speed
        // (speedDrop = velocity * frictionCoefficient * deltaTime), so at 20 it removes roughly a
        // third of the horizontal impulse every tick while grounded, decaying it away almost
        // before it's visible. A jump impulse survives because it makes the character airborne
        // immediately, escaping onto the ~20x gentler air friction - popping the target briefly
        // airborne here does the same thing for knockback, letting the horizontal component
        // actually carry it somewhere instead of being eaten by ground friction on contact.
        //
        // Separate from ApplyDamage rather than a parameter on it - not every hit needs knockback,
        // and the horizontal direction is caller-specific (e.g. a charge continues its momentum
        // forward, a melee/AoE hit pushes radially away from the attacker/blast center), so
        // callers compute their own direction and pass it in rather than this trying to infer one.
        // No-ops if both forces are <= 0, the resolved impulse is zero, the target resists the push
        // entirely, or the target has neither movement component (e.g. a static prop).
        public static void ApplyKnockback(Frame f, EntityRef target, FPVector3 horizontalDirection, FP force,
            FP upwardForce, EntityRef owner, KnockbackApplyMode mode = KnockbackApplyMode.Additive)
        {
            if (force <= FP._0 && upwardForce <= FP._0)
                return;

            FP scale = ResolveKnockbackScale(f, owner, target);

            if (scale <= FP._0)
            {
                Log.Debug($"[Knockback] {target} resisted the push entirely (scale {scale})");
                return;
            }

            ApplyResolvedImpulse(f, target, ResolveImpulse(horizontalDirection, force, upwardForce) * scale, mode);
        }

        // Same scaling/stagger/PhysicsBody-or-KCC push as ApplyKnockback, but for a caller that
        // already has a fully-formed impulse vector rather than a direction+force+upwardForce triple
        // - e.g. JuggernautSkillData.Discharge, whose impulse is built from the caster's own
        // velocity (a magnitude that matters, so it can't be run through ResolveImpulse's
        // normalize-then-scale, which would throw the magnitude away and keep only the direction).
        public static void ApplyKnockbackImpulse(Frame f, EntityRef target, FPVector3 impulse, EntityRef owner,
            KnockbackApplyMode mode = KnockbackApplyMode.Additive)
        {
            FP scale = ResolveKnockbackScale(f, owner, target);

            if (scale <= FP._0)
            {
                Log.Debug($"[Knockback] {target} resisted the push entirely (scale {scale})");
                return;
            }

            ApplyResolvedImpulse(f, target, impulse * scale, mode);
        }

        private static void ApplyResolvedImpulse(Frame f, EntityRef target, FPVector3 impulse,
            KnockbackApplyMode mode = KnockbackApplyMode.Additive)
        {
            if (impulse.SqrMagnitude <= FP._0)
                return;

            bool pushed = false;

            if (f.Unsafe.TryGetPointer<KCC>(target, out var kcc) == true)
            {
                if (mode == KnockbackApplyMode.Override)
                {
                    kcc->SetExternalImpulse(impulse);
                }
                else
                {
                    kcc->AddExternalImpulse(impulse);
                }

                pushed = true;
            }

            if (f.Unsafe.TryGetPointer<PhysicsBody3D>(target, out var body) == true)
            {
                pushed |= PushPhysicsBody(target, body, impulse, mode);
            }

            if (pushed == false)
            {
                Log.Debug($"[Knockback] {target} took no impulse ({impulse}) - no KCC, and no non-kinematic PhysicsBody3D");
                return;
            }

            // Only once the push actually landed: EnemySystem has to stop driving this enemy's
            // velocity for a moment or it would erase the impulse on its next tick.
            if (f.Has<Enemy>(target) == true)
            {
                f.Signals.OnEnemyKnockedBack(target);
            }
        }

        // Vortex's continuous pull, not a one-shot knockback pop - reuses ResolveImpulse/
        // PushPhysicsBody, the same low-level push ApplyKnockback uses, but deliberately skips
        // ResolveKnockbackScale (a vortex's pull shouldn't scale with a character's knockback stats)
        // and never fires OnEnemyKnockedBack/touches Enemy.KnockbackTimer - that signal gates
        // EnemySystem's entire state machine (targeting and attacking included), which would leave a
        // pulled enemy no longer threatening the player at all. See VortexSystem, which separately
        // refreshes Enemy.PullTimer itself so the enemy's own chase movement doesn't erase the pull.
        public static void ApplyPull(Frame f, EntityRef target, FPVector3 direction, FP force)
        {
            if (force <= FP._0)
                return;

            FPVector3 impulse = ResolveImpulse(direction, force, FP._0);

            if (impulse.SqrMagnitude <= FP._0)
                return;

            if (f.Unsafe.TryGetPointer<KCC>(target, out var kcc) == true)
            {
                kcc->AddExternalImpulse(impulse);
            }

            if (f.Unsafe.TryGetPointer<PhysicsBody3D>(target, out var body) == true)
            {
                PushPhysicsBody(target, body, impulse);
            }
        }

        // Y on the incoming direction is dropped rather than normalized along with X/Z: callers pass
        // raw position deltas (attacker->target, radial from a blast center, a charge's momentum),
        // so a height difference between the two would otherwise tilt the push and spend force on
        // lift - a melee hit on an enemy hovering overhead normalized to almost straight up, firing
        // the target higher instead of away. upwardForce is now the only thing that lifts.
        //
        // A target directly overhead or dead-centre on a blast has no horizontal direction to push
        // along, so it takes the lift alone instead of nothing at all.
        private static FPVector3 ResolveImpulse(FPVector3 direction, FP force, FP upwardForce)
        {
            FPVector3 flat = new FPVector3(direction.X, FP._0, direction.Z);
            FPVector3 push = flat.SqrMagnitude > FP._0 ? flat.Normalized * force : FPVector3.Zero;

            return push + FPVector3.Up * upwardForce;
        }

        // Mirrors ResolveOutgoingDamage - the attacker's KnockbackMultiplier and the target's
        // KnockbackTakenMultiplier, both read at impact rather than captured when a shot was fired.
        // Either side without CharacterStats contributes nothing (x1), so enemy attacks and
        // environmental sources keep pushing at exactly their authored force. A target with an
        // Enemy component additionally folds in its EnemyTier's KnockbackMultiplier (see
        // StatusEffectUtility.GetTierResistance) - separate from CanBeInterruptedByKnockback, which
        // gates whether the push interrupts the enemy's AI at all rather than how far it travels.
        // Public so a caller that needs to resolve knockback resistance without going through
        // ApplyKnockback/ApplyKnockbackImpulse's own impulse pipeline (e.g. JuggernautEndExplosion's
        // positional push, which scales a kinematic move distance instead of a physics impulse) can
        // still respect the same KnockbackMultiplier/KnockbackTakenMultiplier stats every other
        // knockback in the game does.
        public static FP ResolveKnockbackScale(Frame f, EntityRef owner, EntityRef target)
        {
            FP scale = FP._1;

            if (f.Unsafe.TryGetPointer<CharacterStats>(owner, out var ownerStats) == true)
            {
                scale *= ownerStats->KnockbackMultiplier;
            }

            if (f.Unsafe.TryGetPointer<CharacterStats>(target, out var targetStats) == true)
            {
                scale *= targetStats->KnockbackTakenMultiplier;
            }

            if (StatusEffectUtility.GetTierResistance(f, target) is { } resistance)
            {
                scale *= resistance.KnockbackMultiplier;
            }

            // Brute's Iron Presence - reduced knockback resistance on Intimidated enemies, stacking
            // with the permanent stat and tier-resistance multipliers above rather than replacing
            // either.
            scale *= StatusEffectUtility.GetKnockbackTakenMultiplier(f, target);

            // Rift-Marked targets take extra knockback, same "stacks with everything above" shape.
            if (StatusEffectUtility.IsRiftMarked(f, target) == true &&
                StatusEffectUtility.GetElementalReactionConfig(f) is { } reactionConfig)
            {
                scale *= reactionConfig.RiftMarkKnockbackMultiplier;
            }

            return scale;
        }

        // KCC.AddExternalImpulse takes a straight velocity delta, but PhysicsBody3D divides an
        // impulse by mass - scaling back up by Mass keeps a single authored Force value meaning the
        // same speed change whether it lands on the player or on an enemy of any mass, so the
        // numbers tuned on KnockbackEffectData/EnemyActionData.Knockback stay comparable across targets.
        //
        // WakeUp is needed because a body that has settled and gone to sleep won't integrate the
        // impulse at all otherwise; a kinematic body never integrates velocity by definition
        // (PhysicsBody3D.ConfigFlags.IsKinematic), so there's nothing to push - enemies run
        // kinematic mid-charge/mid-jump (ChargeDeliveryData, LeapDeliveryData), which makes them
        // immovable for the duration.
        //
        // Override sets Velocity directly instead of adding an impulse for it to integrate next
        // step - deliberately keeps the body's existing vertical speed (falling/jumping) rather
        // than replacing it outright, same as EnemySystem's own ground-plant zeroing
        // (new FPVector3(x, body->Velocity.Y, z)), so an override knockback only ever overrides the
        // horizontal push it's actually meant to replace.
        private static bool PushPhysicsBody(EntityRef target, PhysicsBody3D* body, FPVector3 impulse,
            KnockbackApplyMode mode = KnockbackApplyMode.Additive)
        {
            if (body->IsKinematic == true)
            {
                Log.Debug($"[Knockback] {target} is a kinematic PhysicsBody3D - impulse {impulse} dropped");
                return false;
            }

            body->WakeUp();

            if (mode == KnockbackApplyMode.Override)
            {
                FP y = impulse.Y != FP._0 ? impulse.Y : body->Velocity.Y;
                body->Velocity = new FPVector3(impulse.X, y, impulse.Z);
            }
            else
            {
                body->AddLinearImpulse(impulse * body->Mass);
            }

            Log.Debug($"[Knockback] {target} pushed by {impulse} (PhysicsBody3D, Mass {body->Mass}, {mode})");
            return true;
        }
    }
}
