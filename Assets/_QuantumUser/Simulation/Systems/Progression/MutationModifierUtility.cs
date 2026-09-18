namespace Quantum
{
    using Photon.Deterministic;

    // The single composition point for every LIVE, condition-dependent player modifier a Rift
    // Mutation can grant - the ones that cannot be baked at pick time because the thing they depend
    // on (current health, current Coins, Accessory state, a safe-time streak) changes constantly.
    //
    // One resolver rather than one line per mutation in the damage pipeline: adding a fifth
    // conditional bonus later touches this file only, and DamageUtility keeps exactly one term for
    // the whole category. Every contributor is 0 = off, so a player holding none of them pays one
    // multiply by 1 and nothing else.
    //
    // Deliberately composed MULTIPLICATIVELY, matching how every other multiplier in
    // ResolveOutgoingDamage combines - these are all rare, individually non-stackable mutations, so
    // the compounding case is a deliberately-built rather than accidental stack.
    public static unsafe class MutationModifierUtility
    {
        // Total outgoing-damage multiplier from live conditional mutations. Applies to ALL damage
        // sources, not a Weapon/Skill slice - every mutation feeding it is written as "All Damage".
        public static FP ResolveLiveDamageMultiplier(Frame f, EntityRef owner, CharacterStats* stats)
        {
            FP multiplier = FP._1;

            // Money Talks - your wallet is your weapon.
            multiplier *= FP._1 + CoinUtility.ResolveDamageBonus(stats);

            // Danger Pay - paid for fighting hurt.
            if (IsInDanger(f, owner, stats) == true)
            {
                multiplier *= FP._1 + stats->DangerPayDamageBonus;
            }

            // No Safety Net - nothing between you and the next hit.
            if (stats->NoSafetyNetDamageBonus > FP._0 && AccessoryGuardUtility.IsExposed(f, owner) == true)
            {
                multiplier *= FP._1 + stats->NoSafetyNetDamageBonus;
            }

            // Pressure Cooker - the longer you go untouched, the harder you hit.
            multiplier *= FP._1 + ResolvePressureCookerBonus(stats);

            // Bro Mutation - passive co-op synergy, no combat coordination required. Recomputed live
            // (never baked at pick time) since a teammate picking this up LATER has to retroactively
            // raise everyone who already holds it too.
            if (stats->BroMutationBaseDamageBonus > FP._0)
            {
                int otherOwners = CountOtherBroMutationOwners(f, owner);
                multiplier *= FP._1 + stats->BroMutationBaseDamageBonus + otherOwners * stats->BroMutationPerOwnerDamageBonus;
            }

            return multiplier;
        }

        // Ownership count, not proximity - every connected player who also carries a non-zero
        // BroMutationBaseDamageBonus (i.e. also owns Bro Mutation), alive or not, excluding the
        // player being resolved for. A plain PlayerLink+CharacterStats scan rather than reading
        // RiftMutationPicks/AssetRef directly - Bro Mutation grants no other field, so "do I have the
        // baseline bonus set" already IS "do I own it", the same one-field-is-the-marker idiom every
        // other Rift Mutation in this file already uses.
        private static int CountOtherBroMutationOwners(Frame f, EntityRef owner)
        {
            int count = 0;
            var players = f.Filter<PlayerLink, CharacterStats>();

            while (players.Next(out EntityRef entity, out PlayerLink _, out CharacterStats otherStats))
            {
                if (entity == owner)
                    continue;

                if (otherStats.BroMutationBaseDamageBonus > FP._0)
                {
                    count++;
                }
            }

            return count;
        }

        // Danger Pay's condition, shared by the damage path above and the movement path
        // (PlayerMovementProcessor) so the two can never disagree about whether the player is
        // currently "in danger". Re-evaluated at every read, never snapshotted, which is what makes
        // the bonus vanish the instant healing pushes them back over the line.
        public static bool IsInDanger(Frame f, EntityRef owner, CharacterStats* stats)
        {
            if (stats->DangerPayHealthThreshold <= FP._0)
                return false;

            if (f.Unsafe.TryGetPointer<Health>(owner, out var health) == false || health->MaxHealth <= FP._0)
                return false;

            return health->CurrentHealth / health->MaxHealth < stats->DangerPayHealthThreshold;
        }

        // Danger Pay's movement half. Returns a plain multiplier (1 = unaffected) so
        // PlayerMovementProcessor can fold it in beside MoveSpeedMultiplier and the temporary
        // status buffs with no special-casing.
        public static FP ResolveLiveMoveSpeedMultiplier(Frame f, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(owner, out var stats) == false)
                return FP._1;

            if (stats->DangerPayMoveSpeedBonus <= FP._0 || IsInDanger(f, owner, stats) == false)
                return FP._1;

            return FP._1 + stats->DangerPayMoveSpeedBonus;
        }

        // Pressure Cooker's accumulated bonus. Counts only FULL seconds, so the readout matches the
        // stated "+3% per second" exactly rather than drifting with the tick rate, and is capped.
        public static FP ResolvePressureCookerBonus(CharacterStats* stats)
        {
            if (stats->PressureCookerDamagePerSecond <= FP._0 || stats->PressureCookerMaxBonus <= FP._0)
                return FP._0;

            FP fullSeconds = FPMath.Floor(stats->SafeTimeSeconds);

            if (fullSeconds <= FP._0)
                return FP._0;

            return FPMath.Min(stats->PressureCookerMaxBonus, fullSeconds * stats->PressureCookerDamagePerSecond);
        }
    }
}
