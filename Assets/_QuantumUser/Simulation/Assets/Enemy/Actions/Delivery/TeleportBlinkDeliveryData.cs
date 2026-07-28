namespace Quantum
{
    using Photon.Deterministic;

    // Instant reposition - teleports self to a point offset from the target (e.g. blinking behind
    // them) rather than a fixed distance in the current facing direction. Always instant (Begin()
    // returns true). Deliberately does NOT touch EnemyActionData.Effects - a blink has no natural
    // single target to hit; an action wanting "blink then strike" chains this into a
    // SequenceDeliveryData followed by a real hit delivery instead.
    public unsafe class TeleportBlinkDeliveryData : EnemyDeliveryData
    {
        // Distance from the target this enemy blinks to.
        public FP Distance = 2;

        // True: blinks to the far side of the target as seen from this enemy's current position
        // (i.e. "behind" the target). False: blinks to stand exactly on the target's position.
        public bool BehindTarget = true;

        public override bool Begin(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            if (EnemyMovementUtility.TryGetTargetPosition(f, target, out FPVector3 targetPosition) == false)
                return true;

            FPVector3 destination = targetPosition;

            if (BehindTarget == true)
            {
                FPVector3 fromTarget = filter.Transform3D->Position - targetPosition;
                FPVector3 flatFromTarget = new FPVector3(fromTarget.X, FP._0, fromTarget.Z);

                FPVector3 direction = flatFromTarget.SqrMagnitude > FP._0
                    ? flatFromTarget.Normalized
                    : FPVector3.Forward;

                destination = targetPosition + direction * Distance;
            }

            // Grounded lands on real ground at the destination XZ rather than carrying over
            // whatever Y the enemy happened to be at before blinking; Flying keeps its own height.
            if (data.Stats.Height.InitialState == EnemyHeightState.Grounded &&
                EnemyMovementUtility.TryFindGroundHeight(f, destination, EnemyMovementUtility.GetGroundLayerMask(f), out FP groundY) == true)
            {
                destination.Y = groundY;
            }
            else
            {
                destination.Y = filter.Transform3D->Position.Y;
            }

            filter.Transform3D->Position = destination;

            return true;
        }
    }
}
