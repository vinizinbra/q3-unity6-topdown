namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Decay side of Max's Adrenaline Rush passive - see AdrenalineUtility for the gain side.
    // Stacks stay put until TimeSinceLastGain crosses DecayDelay (out of combat long enough), then
    // lose one stack every DecayInterval - both ticked here rather than in AdrenalineUtility since
    // gain is event-driven (called from DamageUtility) while decay is purely time-based.
    [Preserve]
    public unsafe class AdrenalineSystem : SystemMainThreadFilter<AdrenalineSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            Adrenaline* adrenaline = filter.Adrenaline;

            if (adrenaline->Stacks == 0)
                return;

            adrenaline->TimeSinceLastGain += f.DeltaTime;

            if (adrenaline->TimeSinceLastGain < adrenaline->DecayDelay)
                return;

            // No Time to Breathe - decay is suspended entirely while an enemy is within weapon
            // range, not just slowed, so a sustained fight never bleeds stacks even if no hit has
            // landed in the last DecayDelay seconds (e.g. a long chase or a miss streak).
            if (adrenaline->NoDecayNearWeaponRange == true && IsEnemyInWeaponRange(f, filter.Entity) == true)
                return;

            adrenaline->DecayTimer += f.DeltaTime;

            FP interval = adrenaline->DecayInterval > FP._0 ? adrenaline->DecayInterval : FP._1;

            if (adrenaline->DecayTimer < interval)
                return;

            adrenaline->DecayTimer -= interval;
            adrenaline->Stacks--;
        }

        private static bool IsEnemyInWeaponRange(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<Weapon>(entity, out var weapon) == false)
                return false;

            if (f.Unsafe.TryGetPointer<Transform3D>(entity, out var transform) == false)
                return false;

            WeaponDataAsset weaponData = f.FindAsset(weapon->WeaponData);
            FP range = weaponData.Range * weapon->RangeMultiplier;

            return WeaponPerkUtility.TryFindNearestEnemy(f, transform->Position, range, entity, out _);
        }

        public struct Filter
        {
            public EntityRef Entity;
            public Adrenaline* Adrenaline;
        }
    }
}
