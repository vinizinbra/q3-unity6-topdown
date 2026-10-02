namespace Quantum
{
    using Photon.Deterministic;

    // Leaves Prototype on the ground behind the enemy as it moves - see GroundTrail.qtn. World 2's
    // Fuel Runner drops small StickyPuddles (Scale 0.75).
    public unsafe class GroundTrailPassiveData : EnemyPassiveData
    {
        public AssetRef<EntityPrototype> Prototype;
        public FP SpawnDistance = FP._1_50;
        public FP Duration = FP.FromString("2.5");
        public FP Scale = FP._1;

        public override void Apply(Frame f, EntityRef enemy)
        {
            f.AddOrGet<GroundTrail>(enemy, out var trail);
            trail->Prototype = Prototype;
            trail->SpawnDistance = SpawnDistance;
            trail->Duration = Duration;
            trail->Scale = Scale;
            trail->HasLastDrop = false;
        }
    }
}
