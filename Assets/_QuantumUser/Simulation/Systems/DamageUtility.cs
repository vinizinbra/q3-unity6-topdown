namespace Quantum
{
    using Photon.Deterministic;

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
        public static void ApplyDamage(Frame f, EntityRef target, FP damage, EntityRef owner,
            DamageSource source = DamageSource.None, bool bypassOutgoingResolution = false,
            ElementType element = ElementType.Neutral, bool silent = false)
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
                totalDamage = ResolveOutgoingDamage(f, owner, damage, source, out isCritical);
            }

            // Mark - target-side vulnerability, applied once here so every damage source (hitscan,
            // projectile, melee, DoT ticks) respects it identically. Multiplies the attacker's
            // already-resolved damage rather than replacing Armor/Shield mitigation below it.
            totalDamage *= StatusEffectUtility.GetIncomingDamageMultiplier(f, target);
            totalDamage *= ResolveDamageReduction(f, target);

            FP frontalMultiplier = ResolveFrontalDamageMultiplier(f, target, owner);
            totalDamage *= frontalMultiplier;

            FP remaining = AbsorbWithShield(f, target, ReduceByArmor(f, target, totalDamage));

            health->CurrentHealth = FPMath.Max(FP._0, health->CurrentHealth - remaining);
            f.Events.EntityDamaged(target, owner, totalDamage, isCritical, element, silent, frontalMultiplier < FP._1);

            Log.Debug($"[Damage] {target} took {remaining} to health (raw {damage}, after stats {totalDamage}) " +
                      $"-> {health->CurrentHealth}/{health->MaxHealth}");

            // A landed hit, not just a killing one - an enemy that survives this shot can still go
            // on to die from something else entirely and explode then, same as one that dies right
            // here.
            TryMarkExplodeOnDeath(f, owner, target);

            if (health->CurrentHealth <= FP._0)
            {
                f.Events.EntityDied(target, owner);
                ExperienceUtility.TrySpawnDrop(f, target, owner);

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
                else
                {
                    TrySentryOverload(f, owner, target);
                    f.Destroy(target);
                }
            }
        }

        // Enemies only - a hit on the player or a non-Enemy prop has nothing here to mark. Owner is
        // whoever landed THIS hit (a bullet, a blast, a DoT tick), not necessarily who ends up
        // finishing the target off later - MarkExplosiveDeath is granted by an upgrade shared across
        // heroes (Skills/MarkExplosiveDeathSkillAction), so it doesn't matter whether it was Max's
        // gun or Pixie's bomb that landed this particular hit.
        private static void TryMarkExplodeOnDeath(Frame f, EntityRef owner, EntityRef target)
        {
            if (f.Has<Enemy>(target) == false)
                return;

            if (f.Unsafe.TryGetPointer<MarkExplosiveDeath>(owner, out var mark) == false || mark->Stacks == 0)
                return;

            ExplodeOnDeathConfig config = f.FindAsset(f.RuntimeConfig.ExplodeOnDeathConfig);

            f.AddOrGet<ExplodeOnDeath>(target, out var explode);
            explode->Remaining = config.Duration;
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

            if (damage <= FP._0)
                return;

            HitEffectUtility.ApplyDamageInRadius(f, transform->Position, blastRadius, owner, damage, DamageSource.Skill, config.TargetMask);
            f.Events.ExplodeOnDeathDetonated(owner, transform->Position, blastRadius, enemyData);

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

        // CharacterStats.DamageReduction was already seeded from CharacterData (see
        // CharacterSystem.cs) but never actually read anywhere - this is that missing wire-up. A
        // fraction (0 = no reduction, 1 = fully immune), same convention as every other
        // Multiplier-suffixed stat despite the name; clamped so a stacked bonus past 1 can't flip
        // into negative (healing) damage. Target-side, same as Mark - stacks with it rather than
        // replacing it.
        private static FP ResolveDamageReduction(Frame f, EntityRef target)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(target, out var stats) == false)
                return FP._1;

            return FPMath.Clamp(FP._1 - stats->DamageReduction, FP._0, FP._1);
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

            return damage - absorbed;
        }

        // The attacker's own stats plus whatever weapon it holds. Read from the owner at impact
        // rather than captured when a shot was fired, so a projectile crits by the weapon its
        // shooter holds when it lands. An attacker without CharacterStats deals its damage flat -
        // no multiplier, and no crit, since there'd be nothing to multiply by.
        private static FP ResolveOutgoingDamage(Frame f, EntityRef owner, FP damage, DamageSource source,
            out bool isCritical)
        {
            isCritical = false;

            if (f.Unsafe.TryGetPointer<CharacterStats>(owner, out var stats) == false)
                return damage;

            damage *= stats->DamageMultiplier * GetSourceMultiplier(stats, source);

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
            FP upwardForce, EntityRef owner)
        {
            if (force <= FP._0 && upwardForce <= FP._0)
                return;

            FP scale = ResolveKnockbackScale(f, owner, target);

            if (scale <= FP._0)
            {
                Log.Debug($"[Knockback] {target} resisted the push entirely (scale {scale})");
                return;
            }

            ApplyResolvedImpulse(f, target, ResolveImpulse(horizontalDirection, force, upwardForce) * scale);
        }

        // Same scaling/stagger/PhysicsBody-or-KCC push as ApplyKnockback, but for a caller that
        // already has a fully-formed impulse vector rather than a direction+force+upwardForce triple
        // - e.g. JuggernautSkillData.Discharge, whose impulse is built from the caster's own
        // velocity (a magnitude that matters, so it can't be run through ResolveImpulse's
        // normalize-then-scale, which would throw the magnitude away and keep only the direction).
        public static void ApplyKnockbackImpulse(Frame f, EntityRef target, FPVector3 impulse, EntityRef owner)
        {
            FP scale = ResolveKnockbackScale(f, owner, target);

            if (scale <= FP._0)
            {
                Log.Debug($"[Knockback] {target} resisted the push entirely (scale {scale})");
                return;
            }

            ApplyResolvedImpulse(f, target, impulse * scale);
        }

        private static void ApplyResolvedImpulse(Frame f, EntityRef target, FPVector3 impulse)
        {
            if (impulse.SqrMagnitude <= FP._0)
                return;

            bool pushed = false;

            if (f.Unsafe.TryGetPointer<KCC>(target, out var kcc) == true)
            {
                kcc->AddExternalImpulse(impulse);
                pushed = true;
            }

            if (f.Unsafe.TryGetPointer<PhysicsBody3D>(target, out var body) == true)
            {
                pushed |= PushPhysicsBody(target, body, impulse);
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
        private static bool PushPhysicsBody(EntityRef target, PhysicsBody3D* body, FPVector3 impulse)
        {
            if (body->IsKinematic == true)
            {
                Log.Debug($"[Knockback] {target} is a kinematic PhysicsBody3D - impulse {impulse} dropped");
                return false;
            }

            body->WakeUp();
            body->AddLinearImpulse(impulse * body->Mass);

            Log.Debug($"[Knockback] {target} pushed by {impulse} (PhysicsBody3D, Mass {body->Mass})");
            return true;
        }
    }
}
