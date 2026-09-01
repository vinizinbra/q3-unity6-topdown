namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Drives every SentryBarrel child entity. Two jobs, both every tick, not just once at spawn:
    //
    // 1. Re-anchors this entity to the parent chassis's own CURRENT Position/Rotation + its own
    //    baked WeaponOffset - so if the chassis itself is ever moved after barrels are already
    //    attached (e.g. knocked back), they keep following it and keep their relative mount point
    //    instead of staying pinned to wherever they first spawned.
    //
    // 2. Each barrel then finds its OWN nearest enemy independently, searching from that
    //    just-updated Transform3D.Position, not the chassis's. So 4 barrels mounted at different
    //    points on a sentry can each end up aiming at a different enemy, whichever is actually
    //    closest to that specific muzzle, instead of all 4 slaving to one shared target. Range is
    //    still read from the parent Sentry chassis (not any one barrel's own Weapon), same
    //    reasoning Sentry.Range's own comment already gives - detection range is a property of the
    //    sentry, not of any one gun bolted onto it.
    //
    // Self-destructs the moment its parent Sentry no longer exists (destroyed, expired) rather than
    // tracking its own separate lifetime - a barrel has no reason to outlive the chassis it's bolted
    // onto.
    [Preserve]
    public unsafe class SentryBarrelSystem : SystemMainThreadFilter<SentryBarrelSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            if (f.Unsafe.TryGetPointer<Sentry>(filter.Barrel->Sentry, out var sentry) == false ||
                f.Unsafe.TryGetPointer<Transform3D>(filter.Barrel->Sentry, out var sentryTransform) == false)
            {
                f.Destroy(filter.Entity);
                return;
            }

            FPVector3 position = sentryTransform->Position + sentryTransform->Rotation * filter.Barrel->WeaponOffset;
            filter.Transform3D->Position = position;
            filter.Transform3D->Rotation = sentryTransform->Rotation;

            bool hasTarget = EnemyMovementUtility.TryFindNearestEnemy(f, position, ResolveEngagementRange(f, filter.Entity, sentry), out EntityRef target);

            filter.Aim->Target = target;

            if (hasTarget == true && f.Unsafe.TryGetPointer<Transform3D>(target, out var targetTransform) == true)
            {
                FPVector3 delta = targetTransform->Position - position;
                filter.Aim->Angle = FPMath.Atan2(delta.X, delta.Z) * FP.Rad2Deg;
            }

            filter.InputSource->Data.Fire = hasTarget;

            ApplySentryFireRate(f, filter.Entity, filter.Barrel, sentry);
        }

        // Detection range is a property of the SENTRY (see the class comment above), but the shot
        // itself is capped by this barrel's own WeaponDataAsset.Range - WeaponSystem casts exactly
        // that far and no further, and a projectile expires at exactly that MaxTravelDistance.
        // Nothing kept the two in sync: Sentry.Range is scaled by Fortification's Extended Range and
        // by Lux's own skill-range multiplier, while a barrel's Weapon.RangeMultiplier is only ever
        // touched by player weapon perks and so stays 1 forever. A sentry with any range upgrade
        // therefore locked on, fired every single cooldown, and had the shot die in mid-air short of
        // a target it was visibly aiming at. Engaging at whichever range is actually smaller fixes
        // that without changing what the sentry can DETECT (the Fortification aura and the range
        // indicator ring both still read Sentry.Range directly).
        private static FP ResolveEngagementRange(Frame f, EntityRef barrelEntity, Sentry* sentry)
        {
            if (f.Unsafe.TryGetPointer<Weapon>(barrelEntity, out var weapon) == false)
                return sentry->Range;

            FP weaponRange = WeaponPerkUtility.ResolveWeaponRange(f, weapon);

            // An unauthored/zero weapon Range would otherwise silently disarm the barrel entirely -
            // keep the sentry's own range in that case, which is the pre-existing behaviour.
            return weaponRange > FP._0 ? FPMath.Min(sentry->Range, weaponRange) : sentry->Range;
        }

        // 3. Recomposes this barrel's own Weapon.FireCooldownMultiplier from the sentry-wide fire-rate
        //    multiplier every tick - the single place Overclock, Redline, Field Modification stacks,
        //    Emergency Overclock and Rapid Setup all land, rather than five effects each writing
        //    barrels directly.
        //
        //    Composed against SentryBarrel.BaseFireCooldownMultiplier (captured once at spawn) rather
        //    than against the live value, which is what makes a per-tick write idempotent instead of
        //    compounding without bound - and also what lets a timed multiplier simply lapse and
        //    restore the correct baseline with no revert logic anywhere.
        private static void ApplySentryFireRate(Frame f, EntityRef barrelEntity, SentryBarrel* barrel, Sentry* sentry)
        {
            if (f.Unsafe.TryGetPointer<Weapon>(barrelEntity, out var weapon) == false)
                return;

            FP multiplier = SentryUtility.ResolveFireRateMultiplier(sentry);

            if (multiplier <= FP._0)
                return;

            FP baseMultiplier = barrel->BaseFireCooldownMultiplier > FP._0 ? barrel->BaseFireCooldownMultiplier : FP._1;

            // Fire cooldown is the inverse of fire rate - same division WeaponSystem/
            // FireRateWeaponPerkData already use for a player weapon perk.
            weapon->FireCooldownMultiplier = baseMultiplier / multiplier;
        }

        public struct Filter
        {
            public EntityRef Entity;
            public Transform3D* Transform3D;
            public Aim* Aim;
            public InputSource* InputSource;
            public SentryBarrel* Barrel;
        }
    }
}
