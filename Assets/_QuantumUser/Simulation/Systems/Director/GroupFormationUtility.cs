namespace Quantum
{
    using Photon.Deterministic;

    // Domain 3 (Group Spawner) - turns "member index N of count" into a local offset (relative to
    // the group anchor, before GroupSpawnerUtility's per-spawn facing roll rotates the whole
    // formation) per EnemyGroupConfig.SpawnPattern. Every pattern except Scatter is a pure
    // index/count formula with no RNG draw at all - re-running the same pulse against the same
    // member count always produces the same shape, which is what makes a spawn fully explainable
    // from "which group, which anchor, which pattern" alone. Scatter is the deliberate exception
    // (see its own case below).
    public static unsafe class GroupFormationUtility
    {
        public static FPVector2 ComputeLocalOffset(Frame f, GroupSpawnPattern pattern, int index, int count, FP formationRadius)
        {
            if (count <= 1)
                return FPVector2.Zero;

            switch (pattern)
            {
                case GroupSpawnPattern.Circle:
                    return OnRing(formationRadius, AngleForIndex(index, count, 360));

                case GroupSpawnPattern.Arc:
                    // 120 degree sweep centered on the anchor's forward direction (0 degrees) -
                    // -60..+60, evenly spaced across count members.
                    return OnRing(formationRadius, -60 + (FP)120 * index / (count - 1));

                case GroupSpawnPattern.Line:
                    // Along local X, centered on the anchor - member 0 and member (count-1) sit
                    // exactly FormationRadius to either side.
                    FP lineX = -formationRadius + (formationRadius * 2) * index / (count - 1);
                    return new FPVector2(lineX, FP._0);

                case GroupSpawnPattern.Scatter:
                    // The one pattern that spends an f.RNG roll per member instead of a pure
                    // formula - deliberately "messy" placement (see the design doc's Group
                    // Formation section), still fully deterministic given the same RNG state.
                    FP scatterAngle = f.RNG->Next(0, 360);
                    FP scatterDistance = f.RNG->Next(FP._0, formationRadius);
                    return OnRing(scatterDistance, scatterAngle);

                case GroupSpawnPattern.Cluster:
                default:
                    // Vogel/sunflower packing - a filled disc, not a ring, without ever needing an
                    // RNG draw: each member sits on its own concentric ring at the golden angle
                    // from the previous one, radius growing with sqrt(index/count) so density stays
                    // even from center to edge. Reads as a "tightly packed mob" rather than
                    // Circle's single evenly-spaced ring.
                    FP goldenAngle = 137;
                    FP clusterAngle = index * goldenAngle;
                    FP clusterRadius = formationRadius * FPMath.Sqrt((FP)index / count);
                    return OnRing(clusterRadius, clusterAngle);
            }
        }

        private static FP AngleForIndex(int index, int count, FP sweepDegrees)
        {
            return sweepDegrees * index / count;
        }

        private static FPVector2 OnRing(FP radius, FP angleDegrees)
        {
            FP radians = angleDegrees * FP.Deg2Rad;
            return new FPVector2(FPMath.Sin(radians), FPMath.Cos(radians)) * radius;
        }
    }
}
