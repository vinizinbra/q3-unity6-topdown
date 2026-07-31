namespace Quantum
{
    using Photon.Deterministic;

    // Gain/read side of Max's Adrenaline Rush passive - see Adrenaline.qtn (the component) and
    // AdrenalineSystem (decay). Mirrors StatusEffectUtility's static-utility shape.
    public static unsafe class AdrenalineUtility
    {
        public static void OnDamageDealt(Frame f, EntityRef owner)
        {
            AddStacks(f, owner);
        }

        public static void OnDamageTaken(Frame f, EntityRef target)
        {
            if (AddStacks(f, target) == false)
                return;

            TryApplyTooAngryToDie(f, target);
        }

        private static bool AddStacks(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<Adrenaline>(entity, out var adrenaline) == false)
                return false;

            adrenaline->TimeSinceLastGain = FP._0;

            if (adrenaline->GainPerHit > 0)
            {
                int sum = adrenaline->Stacks + adrenaline->GainPerHit;
                adrenaline->Stacks = (byte)(sum > adrenaline->MaxStacks ? adrenaline->MaxStacks : sum);
            }

            return true;
        }

        // Too Angry to Die - only while genuinely at max stacks (0 DamageReductionAtMax means the
        // ascension hasn't been taken, so this is a no-op either way).
        private static void TryApplyTooAngryToDie(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<Adrenaline>(entity, out var adrenaline) == false)
                return;

            if (adrenaline->DamageReductionAtMax <= FP._0 || adrenaline->Stacks < adrenaline->MaxStacks)
                return;

            StatusEffectUtility.ApplyDamageReduction(f, entity, adrenaline->DamageReductionDuration, adrenaline->DamageReductionAtMax);
        }

        // Folded into StatUtility.GetFireCooldown alongside CharacterStats.AttackSpeedMultiplier and
        // StatusEffectUtility's own Haste - purely a live read off current Stacks, never baked, so
        // there's nothing to revert as Stacks rises and falls.
        public static FP GetFireRateMultiplier(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<Adrenaline>(entity, out var adrenaline) == false)
                return FP._1;

            return FP._1 + adrenaline->Stacks * adrenaline->FireRatePerStack;
        }

        // Folded into PlayerMovementProcessor alongside CharacterStats.MoveSpeedMultiplier and
        // StatusEffectUtility's own Ice slow - same live-read reasoning as GetFireRateMultiplier.
        public static FP GetMoveSpeedMultiplier(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<Adrenaline>(entity, out var adrenaline) == false)
                return FP._1;

            return FP._1 + adrenaline->Stacks * adrenaline->MoveSpeedPerStack;
        }

        // Battle High - only while genuinely at max stacks. 0 WeaponDamageBonusAtMax (the base
        // passive's default) means the ascension hasn't been taken.
        public static FP GetWeaponDamageMultiplier(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<Adrenaline>(entity, out var adrenaline) == false)
                return FP._1;

            if (adrenaline->WeaponDamageBonusAtMax <= FP._0 || adrenaline->Stacks < adrenaline->MaxStacks)
                return FP._1;

            return FP._1 + adrenaline->WeaponDamageBonusAtMax;
        }
    }
}
