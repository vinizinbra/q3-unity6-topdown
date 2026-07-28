namespace Quantum
{
    using Photon.Deterministic;

    public class MovementDataAsset : AssetObject
    {
        public FP WalkSpeed = 4;
        public FP RunSpeed = 8;

        public FPVector3 Gravity = new FPVector3(0, -20, 0);
        public FP MaxGroundAngle = 60;

        public FP JumpMultiplier = 1;

        public FP DynamicGroundFriction = 20;
        public FP KinematicGroundAcceleration = 50;
        public FP KinematicGroundFriction = 35;

        public FP DynamicAirFriction = 2;
        public FP KinematicAirAcceleration = 5;
        public FP KinematicAirFriction = 2;

        // Jump
        public FP JumpVelocity = 8;
        public FP JumpCooldownTime = FP.FromString("0.15");

        // Auto-mantle
        public FP MaxLedgeHeight = 1;
        public FP MantleProbeDistance = FP._0_75;
        public FP AnkleProbeHeight = FP._0_25;

        // Auto-hop (predictive edge check, done while still grounded)
        public FP EdgeProbeDistance = FP._0_50;
        public FP EdgeCheckDistance = 1;
    }
}
