namespace Quantum
{
    using Photon.Deterministic;

    public class MovementDataAsset : AssetObject
    {
        public FP WalkSpeed = 4;
        public FP RunSpeed = 8;

        public FPVector3 Gravity = new FPVector3(0, -20, 0);
        public FP MaxGroundAngle = 60;

        // Terminal velocity - the fastest this character may ever fall, in units/second, applied as
        // a floor on KCCData.DynamicVelocity.Y by PlayerMovementProcessor. Nothing else in the KCC
        // bounds it: EnvironmentProcessor.SetDynamicVelocity adds Gravity * dt every tick and its
        // air friction is XZ-only (new FPVector3(1, 0, 1)), so a character that never lands
        // accelerates downward without limit.
        //
        // That is not just a cosmetic overflow. At this project's 20 Hz tick (SessionConfig
        // UpdateFPS) one tick at -200 u/s is a 10-unit step, and KCC's CCD loop (KCC.Update)
        // subdivides every move into KCCSettings.Radius * CCDRadiusMultiplier = 0.45-unit steps
        // with a full overlap query each - an UNCAPPED while loop, so the cost per tick grows
        // linearly with fall speed for as long as the fall lasts.
        //
        // 30 with the authored Gravity of -45 is reached after roughly 0.7s / 10 units of free
        // fall, which is about where LevelConfig.FallDeathHeight catches a player anyway - so a
        // normal fall off a ledge is unaffected and only the runaway case is bounded. Values <= 0
        // fall back to this same default rather than meaning "unlimited": uncapped is the bug, not
        // a mode anyone wants (see PlayerMovementProcessor.ClampFallSpeed).
        public FP MaxFallSpeed = 30;

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
