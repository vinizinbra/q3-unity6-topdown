namespace Quantum
{
    using UnityEngine.Scripting;

    [Preserve]
    public unsafe class PlayerInitSystem : SystemSignalsOnly, ISignalOnPlayerAdded
    {
        public void OnPlayerAdded(Frame f, PlayerRef player, bool firstTime)
        {
            if (firstTime == false)
                return;

            // Not safe to spawn yet if the level doesn't exist, or chunk colliders haven't had
            // time to settle in physics - LevelGenerationSystem spawns this player itself once
            // both are true instead, via the same PlayerSpawnUtility.Spawn.
            if (PlayerSpawnUtility.IsReadyToSpawn(f) == false)
            {
                Log.Debug($"[LevelGen] player {player} joined before it was safe to spawn - deferring");
                return;
            }

            PlayerSpawnUtility.Spawn(f, player);
        }
    }
}
