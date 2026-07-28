namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Drives every enemy JuggernautSkillData.ApplyEndExplosionPush caught - kinematically walks
    // Transform3D's X/Z from JuggernautExplosionPush.StartPosition to TargetPosition over Duration
    // (Y is never touched, so gravity's own vertical position isn't fought - the enemy just doesn't
    // fall while this runs, same tradeoff ChargeDeliveryData's own kinematic movement already accepts).
    // PhysicsBody3D.IsKinematic is set true the instant this is baked (see ApplyEndExplosionPush) so
    // EnemySystem.Update leaves it alone for the duration - see the JuggernautExplosionPush exemption
    // there, mirroring the existing Active/Charge exemption.
    [Preserve]
    public unsafe class JuggernautExplosionPushSystem : SystemMainThreadFilter<JuggernautExplosionPushSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            filter.Push->Elapsed += f.DeltaTime;

            FP t = filter.Push->Duration > FP._0 ? FPMath.Clamp(filter.Push->Elapsed / filter.Push->Duration, FP._0, FP._1) : FP._1;

            FPVector3 start = filter.Push->StartPosition;
            FPVector3 target = filter.Push->TargetPosition;

            filter.Transform3D->Position = new FPVector3(
                FPMath.Lerp(start.X, target.X, t),
                filter.Transform3D->Position.Y,
                FPMath.Lerp(start.Z, target.Z, t));

            if (t >= FP._1)
            {
                f.Remove<JuggernautExplosionPush>(filter.Entity);

                // Respects Root the same way EnterRecovering does - an enemy that got Rooted mid-push
                // (rare, but possible if Root's own chance happened to proc on an unrelated landing
                // during these few ticks) should stay kinematic instead of this handing physics back.
                filter.PhysicsBody3D->IsKinematic = StatusEffectUtility.IsRooted(f, filter.Entity);
            }
        }

        public struct Filter
        {
            public EntityRef Entity;
            public Transform3D* Transform3D;
            public PhysicsBody3D* PhysicsBody3D;
            public JuggernautExplosionPush* Push;
        }
    }
}
