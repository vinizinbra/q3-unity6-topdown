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

            bool hasTarget = EnemyMovementUtility.TryFindNearestEnemy(f, position, sentry->Range, out EntityRef target);

            filter.Aim->Target = target;

            if (hasTarget == true && f.Unsafe.TryGetPointer<Transform3D>(target, out var targetTransform) == true)
            {
                FPVector3 delta = targetTransform->Position - position;
                filter.Aim->Angle = FPMath.Atan2(delta.X, delta.Z) * FP.Rad2Deg;
            }

            filter.InputSource->Data.Fire = hasTarget;
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
