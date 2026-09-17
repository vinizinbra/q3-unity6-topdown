namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;

    // Pixie's Heavy Mastery R3 "Rocket Conversion" area-expansion half (docs/hero-mastery.md) - generic
    // and hero-agnostic: any owner holding HeavyHitAreaExpansionUpgrade while a Heavy weapon is
    // equipped gets every Weapon-sourced hit's effective area grown by ExtraRadius, whether the hit
    // already had an area of its own (AreaHitData.Detonate adds it straight onto BlastRadius) or was a
    // single-target hit (TryExpandSingleTargetHit below gives it a brand new radius around the impact
    // point). The primary target already took the shot's own hit/status a moment earlier in the same
    // call chain, so it is always excluded here - never a second, duplicate damage event against it.
    public static unsafe class HeavyHitAreaUtility
    {
        // Additive world-space radius bonus for the owner's current Heavy weapon, or 0 if the upgrade
        // isn't held, no weapon is equipped, or the equipped weapon isn't currently Heavy. Callers gate
        // this on DamageSource.Weapon themselves (same convention as every other Hero Mastery term),
        // since this utility only ever cares about the owner's live equipped WeaponDataAsset.Weight.
        public static FP ResolveExtraRadius(Frame f, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<HeavyHitAreaExpansionUpgrade>(owner, out var upgrade) == false)
                return FP._0;

            if (f.Unsafe.TryGetPointer<Weapon>(owner, out var weapon) == false || weapon->WeaponData.IsValid == false)
                return FP._0;

            return f.FindAsset(weapon->WeaponData).Weight == WeaponWeight.Heavy ? upgrade->ExtraRadius : FP._0;
        }

        // For a hit that had no area of its own (a hitscan contact or a DirectHitData projectile) -
        // spawns a fresh ExtraRadius-sized area around the impact point, damaging and applying the
        // weapon's own element to every OTHER enemy caught. primaryTarget is always skipped - it
        // already took this exact hit's own damage/status a moment earlier in the same call chain, so
        // including it here would double both. No-op if the owner doesn't hold the upgrade or isn't
        // currently wielding a Heavy weapon.
        public static void TryExpandSingleTargetHit(Frame f, EntityRef owner, EntityRef primaryTarget,
            FPVector3 point, ElementType element, FP damage)
        {
            FP radius = ResolveExtraRadius(f, owner);

            if (radius <= FP._0)
                return;

            Shape3D sphere = Shape3D.CreateSphere(radius);
            var hits = f.Physics3D.OverlapShape(point, FPQuaternion.Identity, sphere, -1, QueryOptions.HitAll);

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef target = hits[i].Entity;

                if (target == EntityRef.None || target == owner || target == primaryTarget || f.Has<Enemy>(target) == false)
                    continue;

                DamageUtility.ApplyDamage(f, target, damage, owner, DamageSource.Weapon);
                StatusEffectUtility.TryApplyElementalStatus(f, target, owner, DamageSource.Weapon, element, damage);
            }

            f.Events.WeaponExplosionReleased(owner, point, radius);
        }
    }
}
