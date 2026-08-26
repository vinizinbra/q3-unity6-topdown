namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;

    // Shared helpers for Brute's Ascension lines - see docs/brute-ascensions.md.
    public static unsafe class BruteAscensionUtility
    {
        // "Juggernaut Skill Damage" - the percentage basis Aftershock/Concussive Impact/Iron Shoulder
        // all scale off (see JuggernautSkillData.Damage's own comment). Mirrors
        // PixieAscensionUtility.ResolveBunnyBombDamage exactly - resolved via CharacterStats.
        // CharacterData -> CharacterData.HeroSkill rather than a direct asset reference, so it always
        // reflects whichever hero/skill asset the entity actually has equipped.
        //
        // Returns the BASE damage value, not run through DamageUtility.ResolveOutgoingDamage - every
        // caller feeds its own computed number into DamageUtility.ApplyDamage, which resolves the full
        // live multiplier stack (CharacterStats.DamageMultiplier, crit, Stun Damage Bonus, etc.) exactly
        // once. Resolving it here too would double-apply those multipliers. Deliberately does NOT fold
        // in Bone Breaker's own multiplier - that bonus is scoped to Discharge's own hit damage only
        // (see JuggernautSkillData.Discharge), so Aftershock/Concussive Impact/Iron Shoulder always
        // scale off the same raw baseline regardless of Bone Breaker's rank.
        public static FP ResolveJuggernautSkillDamage(Frame f, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(owner, out var stats) == false || stats->CharacterData.IsValid == false)
                return FP._0;

            CharacterData data = f.FindAsset(stats->CharacterData);

            if (data.HeroSkill.IsValid == false)
                return FP._0;

            if (f.FindAsset(data.HeroSkill) is not JuggernautSkillData juggernautSkill)
                return FP._0;

            return juggernautSkill.Damage;
        }

        // Generic "damage + stun everyone in radius" sweep - used by Concussive Impact rank 3's
        // landing shockwave and Iron Shoulder rank 3's wall-slam shockwave alike, so neither needed its
        // own copy of the same OverlapShape/Enemy-gate loop. Either damage or stunDuration can be 0 to
        // skip that half (e.g. Aftershock's rank-3 stun-only pulse, where damage was already applied
        // separately by its own end-explosion radius). Mirrors ExplodeOnDestroyUtility.
        // ForceMarkEnemiesInRadius's own shape.
        public static void ApplyRadialStunDamage(Frame f, FPVector3 center, FP radius, EntityRef owner, FP damage, FP stunDuration)
        {
            if (radius <= FP._0)
                return;

            Shape3D sphere = Shape3D.CreateSphere(radius);
            var hits = f.Physics3D.OverlapShape(center, FPQuaternion.Identity, sphere, -1, QueryOptions.HitAll);

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef target = hits[i].Entity;

                if (f.Has<Enemy>(target) == false)
                    continue;

                if (damage > FP._0)
                {
                    DamageUtility.ApplyDamage(f, target, damage, owner, DamageSource.Skill);
                }

                if (stunDuration > FP._0)
                {
                    StatusEffectUtility.ApplyStun(f, target, stunDuration, owner);
                }
            }
        }

        // Knockback-only sibling of ApplyRadialStunDamage - pushes every enemy in radius radially away
        // from center, with no damage and no stun. Bodyguard rank 3's shockwave (fired around the ALLY
        // whose Free Hit Guard just saved them, not around Brute) is the first caller: its job is to
        // buy that ally breathing room the instant they nearly died, so adding damage would quietly
        // turn a defensive payoff into a damage line.
        //
        // Routes through DamageUtility.ApplyKnockback rather than writing velocity directly, so tier
        // knockback resistance, Iron Presence's knockback bonus and the enemy stuck-recovery window
        // (EnemyStuckRecoveryUtility) all apply exactly as they do for every other push Brute makes.
        public static void ApplyRadialKnockback(Frame f, FPVector3 center, FP radius, EntityRef owner, FP force)
        {
            if (radius <= FP._0 || force <= FP._0)
                return;

            Shape3D sphere = Shape3D.CreateSphere(radius);
            var hits = f.Physics3D.OverlapShape(center, FPQuaternion.Identity, sphere, -1, QueryOptions.HitAll);

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef target = hits[i].Entity;

                if (f.Has<Enemy>(target) == false)
                    continue;

                if (f.Unsafe.TryGetPointer<Transform3D>(target, out var targetTransform) == false)
                    continue;

                FPVector3 away = targetTransform->Position - center;
                away.Y = FP._0;

                // An enemy standing exactly on the center has no direction to be pushed in - skip it
                // rather than normalizing a zero vector into garbage.
                if (away.SqrMagnitude <= FP._0)
                    continue;

                DamageUtility.ApplyKnockback(f, target, away.Normalized, force, FP._0, owner);
            }
        }
    }
}
