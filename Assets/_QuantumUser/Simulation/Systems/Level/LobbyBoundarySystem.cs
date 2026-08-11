namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Lobby Start - the run doesn't actually begin (see CombatDirectorSystem's own GameState gate)
    // until every connected, spawned player has physically walked outside the LobbyStart chunk's
    // own footprint. No separate hand-placed boundary entity - the chunk IS the boundary, read
    // back via LevelGenerationSystem.TryGetLobbyStartBounds the same way SpawnAtBossArenaDirectly
    // reads the Boss Arena's own footprint. Unfiltered SystemMainThread (like
    // TalentGateSystem/LevelGenerationSystem) - resolves a single Global value from world state,
    // not per-entity logic of its own. Must run before CombatDirectorSystem (inside
    // GameplaySystemGroup, later this same tick) so GameState.Survival is current when that
    // gate checks it - see SystemSetup.User.cs.
    [Preserve]
    public unsafe class LobbyBoundarySystem : SystemMainThread
    {
        public override void Update(Frame f)
        {
            if (f.Global->CurrentState != GameState.Lobby)
                return;

            for (int i = 0; i < f.PlayerCount; i++)
            {
                // Same "skip an unjoined slot, wait for every joined one" guard TalentGateSystem
                // uses - a player who hasn't spawned yet hasn't left the lobby either.
                PlayerRef player = i;

                if (f.GetPlayerData(player) == null)
                    continue;

                if (PlayerSpawnUtility.HasSpawned(f, player) == false)
                    return;
            }

            if (LevelGenerationSystem.TryGetLobbyStartBounds(f, out FPVector3 min, out FPVector3 max) == false)
                return; // no LobbyStart chunk placed yet - nothing to check against

            var players = f.Filter<PlayerLink, Transform3D>();

            while (players.Next(out EntityRef _, out PlayerLink _, out Transform3D playerTransform))
            {
                if (IsInsideFootprint(playerTransform.Position, min, max))
                    return; // at least one player is still inside the LobbyStart footprint
            }

            GameStateUtility.SetState(f, GameState.Survival);
            Log.Debug("[Talents] GameState.Survival - Director/spawning unlocked");
        }

        // X/Z only - Y (height) isn't part of a chunk's footprint, same convention CellToWorld/
        // FootprintCenterToWorld use (floor stays at Y=0).
        private static bool IsInsideFootprint(FPVector3 position, FPVector3 min, FPVector3 max)
        {
            return position.X >= min.X && position.X <= max.X && position.Z >= min.Z && position.Z <= max.Z;
        }
    }
}
