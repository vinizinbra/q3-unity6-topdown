namespace Quantum
{
    using UnityEngine.Scripting;

    // GroundOffset counterpart to SpawnedEntitySpawner.Apply for entities placed directly in the map
    // asset (a Chest, see QuantumMap.asset) rather than created through a skill/projectile spawn - a
    // map-baked entity never goes through SpawnedEntitySpawner.Spawn, so without this its authored
    // GroundOffset would sit inert and it'd just hang at whatever raw Transform3D.Position.Y was
    // hand-placed in the editor. MapEntityLink (added implicitly by Quantum to every entity resolved
    // from the map, never present on an f.Create(prototype) spawn) is the gate that scopes this to
    // map-baked entities only, so it can't double-apply GroundOffset for something
    // SpawnedEntitySpawner already ground-checked at its own actual spawn position.
    [Preserve]
    public unsafe class MapGroundSettleSystem : SystemSignalsOnly, ISignalOnEntityPrototypeMaterialized
    {
        public void OnEntityPrototypeMaterialized(Frame f, EntityRef entity, EntityPrototypeRef prototypeRef)
        {
            if (f.Unsafe.TryGetPointer<MapEntityLink>(entity, out _) == false)
                return;

            if (f.Unsafe.TryGetPointer<GroundOffset>(entity, out _) == false)
                return;

            if (f.Unsafe.TryGetPointer<Transform3D>(entity, out var transform) == false)
                return;

            GroundOffsetUtility.Apply(f, entity, transform);
        }
    }
}
