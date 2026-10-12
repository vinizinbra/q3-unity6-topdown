namespace Quantum
{
    // Spawn side of a specific enemy's own designed Chest drop (EnemyDataAsset.ChestDrop) - e.g. a
    // mini-boss/rare spawn guaranteed (or chance-rolled) to leave a Chest behind. Unlike Coin/
    // RiftShard/Exp (a fungible currency every enemy of a tier can drop from a shared
    // RuntimeConfig.Prefabs pickup), this is a per-enemy-asset one-off, so it spawns whichever
    // EntityPrototype that enemy's own asset points at directly - same "f.Create -> set Position ->
    // GroundOffsetUtility.Apply" runtime-spawn idiom TalentGateSystem already uses for a Chest
    // referenced from a ChunkSpawnConfig (see docs/chests.md's own "Editor authoring needed"
    // exception for a talent-gated Chest).
    public static unsafe class EnemyChestDropUtility
    {
        // Called from DamageUtility.ApplyDamage right alongside ExperienceUtility/ScrapUtility/
        // RiftShardUtility/CoinUtility's own TrySpawnDrop calls. position = the corpse, or the
        // nearest platform for a fall death (see DamageUtility.ResolveDeath).
        public static void TrySpawnDrop(Frame f, EntityRef target, Photon.Deterministic.FPVector3 position)
        {
            if (f.Unsafe.TryGetPointer<Enemy>(target, out var enemy) == false)
                return;

            EnemyDataAsset data = f.FindAsset(enemy->EnemyData);

            if (data.ChestDrop.IsValid == false)
                return;

            if (DamageUtility.RollChance(f, data.ChestDropChance) == false)
                return;

            EntityRef chest = f.Create(data.ChestDrop);

            if (f.Unsafe.TryGetPointer<Transform3D>(chest, out var chestTransform) == true)
            {
                chestTransform->Position = position;
                GroundOffsetUtility.Apply(f, chest);
            }

            Log.Debug($"[Chest] {target} ({data.EnemyName}) dropped Chest {chest} at {position}");
        }
    }
}
