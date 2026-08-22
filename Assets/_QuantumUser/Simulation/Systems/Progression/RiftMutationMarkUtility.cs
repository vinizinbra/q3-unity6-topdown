namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;

    // Evaluates the Rift Mutation content pool's mark-application conditions (see
    // docs/rift-mutations.md for the full 11-mutation roster and "Event resolution order") - no
    // weapon- or hero-specific hardcoded logic lives here, every check reads a plain CharacterStats
    // flag baked at pick time (see each RiftMutationData subclass's own Apply) and
    // ElementalReactionConfig's own tunables. Rift Dash is the one exception (lives in
    // DashSkillData/RiftDashMarkTracker instead - not damage-hook-shaped) and Overflowing Rift (lives
    // in RiftMarkApplicationUtility.ApplyRequest instead - triggers on application, not on damage).
    public static unsafe class RiftMutationMarkUtility
    {
        // Called once per resolved hit from DamageUtility.ApplyDamage, after damage/crit are resolved
        // but before health is subtracted (so preDamageCurrentHealth/MaxHealth are both still live) -
        // already gated by the caller to exclude DoT-tick replays (bypassOutgoingResolution).
        //
        // Evaluates every qualifying mutation in a fixed priority order (most narrow/specific first)
        // and requests AT MOST ONE application - "prefer one Rift Mark application per hit event",
        // see docs/rift-mutations.md's "Duplicate-application policy". A coincidental overlap between
        // this function and a separate Weapon Perk on the same physical hit is a known MVP
        // simplification, not deduped here (Critical Fracture is the one exception, since the perk and
        // this mutation share a single cooldown key - see RiftMarkCooldownKey's own comment).
        public static void EvaluateOnDamage(Frame f, EntityRef target, EntityRef owner, DamageSource source,
            FP damage, FP preDamageCurrentHealth, FP preDamageMaxHealth, bool isCritical)
        {
            if (source != DamageSource.Weapon && source != DamageSource.Skill)
                return;

            if (owner == EntityRef.None || target == EntityRef.None || owner == target)
                return;

            if (f.Unsafe.TryGetPointer<CharacterStats>(owner, out var stats) == false)
                return;

            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false)
                return;

            ElementalReactionConfig config = StatusEffectUtility.GetElementalReactionConfig(f);

            if (config == null)
                return;

            if (TryFirstContact(f, status, stats, target, owner, preDamageCurrentHealth, preDamageMaxHealth, config))
                return;

            if (TryExecutionFracture(f, status, stats, target, owner, preDamageCurrentHealth, preDamageMaxHealth, config))
                return;

            if (source == DamageSource.Skill && TrySkillFracture(f, status, stats, target, owner, config))
                return;

            if (isCritical && TryCriticalFracture(f, status, stats, target, owner, config))
                return;

            if (TryHeavyFracture(f, status, stats, target, owner, damage, preDamageMaxHealth, config))
                return;

            TryCloseOrLongFracture(f, status, stats, target, owner, config);
        }

        // First Contact - one-time flag, not a cooldown. Only ever fires if the specific hit that
        // happens to land first against a full-health target ALSO comes from a mutation-holding
        // player - a full-health hit from someone WITHOUT the mutation leaves the target no longer
        // full-health afterward, naturally closing the window without needing to touch the flag.
        private static bool TryFirstContact(Frame f, StatusEffects* status, CharacterStats* stats,
            EntityRef target, EntityRef owner, FP preHealth, FP maxHealth, ElementalReactionConfig config)
        {
            if (stats->HasFirstContactMutation == false || status->FirstContactTriggered == true)
                return false;

            if (maxHealth <= FP._0 || preHealth < maxHealth)
                return false;

            status->FirstContactTriggered = true;
            RequestAndApply(f, target, owner, RiftMarkApplicationSource.MutationFirstContact, config);
            return true;
        }

        // Execution Fracture - checked against health BEFORE this hit's own damage, per
        // docs/rift-mutations.md's explicit "must already be below the threshold when the hit begins
        // resolving" rule (a hit that itself brings the target below threshold does not qualify).
        private static bool TryExecutionFracture(Frame f, StatusEffects* status, CharacterStats* stats,
            EntityRef target, EntityRef owner, FP preHealth, FP maxHealth, ElementalReactionConfig config)
        {
            if (stats->HasExecutionFractureMutation == false)
                return false;

            if (IsBelowExecutionThreshold(preHealth, maxHealth, config.ExecutionHealthThreshold) == false)
                return false;

            if (RiftMarkApplicationUtility.TryConsumeCooldown(status, RiftMarkCooldownKey.ExecutionFracture, config.StandardMarkApplicationCooldown) == false)
                return false;

            RequestAndApply(f, target, owner, RiftMarkApplicationSource.MutationExecutionFracture, config);
            return true;
        }

        // Skill Fracture - "Hero Skill hits apply 1 Rift Mark" - source == Skill is already checked
        // by the caller before this runs. A persistent field/DoT/repeated pulse still qualifies each
        // time it lands (same as any other Skill-sourced hit), but the shared per-target cooldown
        // below is what stops it from reapplying on every single tick.
        private static bool TrySkillFracture(Frame f, StatusEffects* status, CharacterStats* stats,
            EntityRef target, EntityRef owner, ElementalReactionConfig config)
        {
            if (stats->HasSkillFractureMutation == false)
                return false;

            if (RiftMarkApplicationUtility.TryConsumeCooldown(status, RiftMarkCooldownKey.SkillFracture, config.StandardMarkApplicationCooldown) == false)
                return false;

            RequestAndApply(f, target, owner, RiftMarkApplicationSource.MutationSkillFracture, config);
            return true;
        }

        // Critical Fracture (mutation half) - shares RiftMarkCooldownKey.CriticalFracture with the
        // Weapon Perk version (WeaponPerkReactionSystem.OnCriticalHit) so the two can never both
        // stack from the same crit - see RiftMarkCooldownKey's own comment.
        private static bool TryCriticalFracture(Frame f, StatusEffects* status, CharacterStats* stats,
            EntityRef target, EntityRef owner, ElementalReactionConfig config)
        {
            if (stats->HasCriticalFractureMutation == false)
                return false;

            if (RiftMarkApplicationUtility.TryConsumeCooldown(status, RiftMarkCooldownKey.CriticalFracture, config.StandardMarkApplicationCooldown) == false)
                return false;

            RequestAndApply(f, target, owner, RiftMarkApplicationSource.MutationCriticalFracture, config);
            return true;
        }

        // Heavy Fracture - qualifies on EITHER a flat damage threshold OR a percent-of-target's-own-
        // MaxHealth threshold (whichever the hit clears first) - evaluated against this one resolved
        // hit's own damage, never aggregated across multiple hits.
        private static bool TryHeavyFracture(Frame f, StatusEffects* status, CharacterStats* stats,
            EntityRef target, EntityRef owner, FP damage, FP maxHealth, ElementalReactionConfig config)
        {
            if (stats->HasHeavyFractureMutation == false)
                return false;

            if (IsHeavyHit(damage, maxHealth, config.HeavyHitDamageThreshold, config.HeavyHitHealthPercentThreshold) == false)
                return false;

            if (RiftMarkApplicationUtility.TryConsumeCooldown(status, RiftMarkCooldownKey.HeavyFracture, config.StandardMarkApplicationCooldown) == false)
                return false;

            RequestAndApply(f, target, owner, RiftMarkApplicationSource.MutationHeavyFracture, config);
            return true;
        }

        // Pure - qualifies on EITHER a flat damage threshold OR a percent-of-target's-own-MaxHealth
        // threshold. Factored out of TryHeavyFracture so it's covered by plain EditMode tests (see
        // Assets/_QuantumUser/Editor/Tests/RiftMarkApplicationTests.cs) without needing a live Frame.
        public static bool IsHeavyHit(FP damage, FP maxHealth, FP flatThreshold, FP percentThreshold)
        {
            if (damage >= flatThreshold)
                return true;

            return maxHealth > FP._0 && damage / maxHealth >= percentThreshold;
        }

        // Pure - health checked BEFORE this hit's own damage is subtracted (see TryExecutionFracture's
        // own comment for why). maxHealth <= 0 (unseeded Health) never qualifies. Factored out for the
        // same testability reason as IsHeavyHit above.
        public static bool IsBelowExecutionThreshold(FP preHealth, FP maxHealth, FP threshold)
        {
            return maxHealth > FP._0 && preHealth / maxHealth < threshold;
        }

        // Close/Long Fracture - plain FPVector3.Distance (not squared) against a linear threshold,
        // matching DamageUtility.ResolveRangeDamageMultiplier's own convention for this exact kind of
        // near/far threshold-band check. Mutually exclusive by construction (near vs far), Close
        // checked first when a target could somehow be both (shouldn't happen with sane thresholds).
        private static bool TryCloseOrLongFracture(Frame f, StatusEffects* status, CharacterStats* stats,
            EntityRef target, EntityRef owner, ElementalReactionConfig config)
        {
            if (stats->HasCloseFractureMutation == false && stats->HasLongFractureMutation == false)
                return false;

            if (f.Unsafe.TryGetPointer<Transform3D>(target, out var targetTransform) == false ||
                f.Unsafe.TryGetPointer<Transform3D>(owner, out var ownerTransform) == false)
                return false;

            FP distance = FPVector3.Distance(ownerTransform->Position, targetTransform->Position);

            if (stats->HasCloseFractureMutation == true && distance <= config.CloseRangeThreshold)
            {
                if (RiftMarkApplicationUtility.TryConsumeCooldown(status, RiftMarkCooldownKey.CloseFracture, config.StandardMarkApplicationCooldown) == false)
                    return false;

                RequestAndApply(f, target, owner, RiftMarkApplicationSource.MutationCloseFracture, config);
                return true;
            }

            if (stats->HasLongFractureMutation == true && distance >= config.LongRangeThreshold)
            {
                if (RiftMarkApplicationUtility.TryConsumeCooldown(status, RiftMarkCooldownKey.LongFracture, config.StandardMarkApplicationCooldown) == false)
                    return false;

                RequestAndApply(f, target, owner, RiftMarkApplicationSource.MutationLongFracture, config);
                return true;
            }

            return false;
        }

        // Last Stand - the PLAYER's own received hit, not an enemy taking one, so this is called
        // separately from EvaluateOnDamage above (which only ever looks at enemy-received hits) -
        // see DamageUtility.ApplyDamage's own call site. Threshold is flat damage; cooldown is
        // per-player (CharacterStats.LastStandCooldownRemaining), not per-target.
        public static void EvaluateLastStand(Frame f, EntityRef target, FP damage)
        {
            if (f.Has<PlayerLink>(target) == false)
                return;

            if (f.Unsafe.TryGetPointer<CharacterStats>(target, out var stats) == false || stats->HasLastStandMutation == false)
                return;

            if (stats->LastStandCooldownRemaining > FP._0)
                return;

            ElementalReactionConfig config = StatusEffectUtility.GetElementalReactionConfig(f);

            if (config == null || config.LastStandThreshold <= FP._0 || damage < config.LastStandThreshold)
                return;

            if (f.Unsafe.TryGetPointer<Transform3D>(target, out var transform) == false)
                return;

            stats->LastStandCooldownRemaining = config.LastStandCooldown;

            Shape3D sphere = Shape3D.CreateSphere(config.LastStandPulseRadius);
            var hits = f.Physics3D.OverlapShape(transform->Position, FPQuaternion.Identity, sphere, -1, QueryOptions.HitAll);

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef hitEntity = hits[i].Entity;

                // The pulse applies no mark to the player themselves - only enemies caught in it.
                if (hitEntity == target || f.Has<Enemy>(hitEntity) == false)
                    continue;

                RequestAndApply(f, hitEntity, target, RiftMarkApplicationSource.MutationLastStand, config);
            }

            Log.Debug($"[RiftMark] {target}'s Last Stand pulse triggered (radius {config.LastStandPulseRadius})");
        }

        // Fractured Presence - called once per StatusEffects-bearing entity per tick from
        // StatusEffectSystem.Update, tracking every nearby mutation-holding player's own exposure
        // slot on the TARGET (an enemy) rather than on the player, since one enemy can be exposed to
        // several players independently. O(enemies * players) every tick - acceptable at this
        // project's player count (co-op, small fixed cap), not spatially partitioned.
        public static void TickFracturedPresence(Frame f, EntityRef target, StatusEffects* status)
        {
            if (f.Has<Enemy>(target) == false)
                return;

            // Nobody in the run has the mutation - by far the common case, since it is one pick out
            // of a 25-mutation catalog. Bails before the Transform lookup, the RuntimeConfig
            // FindAsset and the wider 3-component player scan below, all of which otherwise ran for
            // EVERY StatusEffects-bearing entity EVERY tick to do nothing. Skipping cannot leave a
            // stale exposure slot behind: a slot is only ever written while a player actually holds
            // the mutation, and a holder never loses it.
            if (AnyPlayerHasFracturedPresence(f) == false)
                return;

            if (f.Unsafe.TryGetPointer<Transform3D>(target, out var targetTransform) == false)
                return;

            ElementalReactionConfig config = StatusEffectUtility.GetElementalReactionConfig(f);

            if (config == null)
                return;

            FP radiusSqr = config.FracturedPresenceRadius * config.FracturedPresenceRadius;

            var filtered = f.Filter<PlayerLink, CharacterStats, Transform3D>();
            while (filtered.Next(out EntityRef player, out PlayerLink _, out CharacterStats playerStats, out Transform3D playerTransform))
            {
                if (playerStats.HasFracturedPresenceMutation == false)
                {
                    ClearFracturedPresenceSlot(status, player);
                    continue;
                }

                FP sqrDistance = (playerTransform.Position - targetTransform->Position).SqrMagnitude;

                if (sqrDistance > radiusSqr)
                {
                    ClearFracturedPresenceSlot(status, player);
                    continue;
                }

                int slot = FindOrAssignFracturedPresenceSlot(status, player);

                if (slot < 0)
                    continue; // every slot already claimed by other players this tick - dropped, not evicted

                status->FracturedPresenceExposureTime[slot] += f.DeltaTime;

                if (status->FracturedPresenceExposureTime[slot] < config.FracturedPresenceExposureTime)
                    continue;

                if (RiftMarkApplicationUtility.TryConsumeCooldown(status, RiftMarkCooldownKey.FracturedPresence, config.StandardMarkApplicationCooldown) == false)
                    continue;

                status->FracturedPresenceExposureTime[slot] = FP._0;
                RequestAndApply(f, target, player, RiftMarkApplicationSource.MutationFracturedPresence, config);
            }
        }

        // Deliberately its own tiny scan rather than a cached Global flag - <= 4 players, and a
        // cached flag would be one more piece of rollback state to keep honest.
        private static bool AnyPlayerHasFracturedPresence(Frame f)
        {
            var filtered = f.Filter<PlayerLink, CharacterStats>();

            while (filtered.Next(out EntityRef _, out PlayerLink _, out CharacterStats stats))
            {
                if (stats.HasFracturedPresenceMutation == true)
                    return true;
            }

            return false;
        }

        private static int FindOrAssignFracturedPresenceSlot(StatusEffects* status, EntityRef player)
        {
            for (int i = 0; i < 4; i++)
            {
                if (status->FracturedPresenceExposedBy[i] == player)
                    return i;
            }

            for (int i = 0; i < 4; i++)
            {
                if (status->FracturedPresenceExposedBy[i] == EntityRef.None)
                {
                    status->FracturedPresenceExposedBy[i] = player;
                    status->FracturedPresenceExposureTime[i] = FP._0;
                    return i;
                }
            }

            return -1;
        }

        private static void ClearFracturedPresenceSlot(StatusEffects* status, EntityRef player)
        {
            for (int i = 0; i < 4; i++)
            {
                if (status->FracturedPresenceExposedBy[i] != player)
                    continue;

                status->FracturedPresenceExposedBy[i] = EntityRef.None;
                status->FracturedPresenceExposureTime[i] = FP._0;
                return;
            }
        }

        private static void RequestAndApply(Frame f, EntityRef target, EntityRef owner, RiftMarkApplicationSource source, ElementalReactionConfig config)
        {
            var request = new RiftMarkApplicationRequest
            {
                Source = owner,
                Target = target,
                HitSequence = f.Number,
                ApplicationSource = source,
                RequestedStacks = config.StacksAppliedPerApplication,
                Owner = owner,
                CooldownKey = RiftMarkCooldownKey.None,
            };

            RiftMarkApplicationUtility.ApplyRequest(f, request, config);
        }
    }
}
