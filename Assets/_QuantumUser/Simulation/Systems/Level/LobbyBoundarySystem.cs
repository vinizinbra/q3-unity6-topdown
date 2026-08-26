namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Lobby Start - the run doesn't actually begin (see CombatDirectorSystem's own GameState gate)
    // until the level has finished generating, at least one player has actually joined, every
    // connected player has spawned, and ANY ONE of them has physically walked outside the LobbyStart
    // chunk's own footprint. Deliberately first-one-out, not everyone-out: a co-op party shouldn't
    // be held in the lobby by whoever is slowest to walk, and the run starting is what everyone is
    // waiting on. The other guards are not paperwork - the footprint scan needs a real, fully placed
    // world and at least one spawned player before "someone is outside" means anything - see Update.
    // No separate hand-placed boundary entity - the chunk IS the boundary, read
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

            // TryGetLobbyStartBounds builds its AABB around Global.PlayerSpawnPosition, which isn't
            // assigned until the very last step of generation (LevelGenerationSystem
            // .AssignPlayerSpawnPosition). Read any earlier it's still default (0,0,0), so the bounds
            // describe a box around the world origin rather than the actual lobby - and a hand-placed
            // LobbyStart chunk (picked up by SeedFromExistingChunks from frame 0) makes the call
            // succeed with exactly those bogus bounds. Same precondition TalentGateSystem needs, for
            // the same reason: nothing here is meaningful until the level is fully placed.
            if (f.Global->LevelGenerated == false)
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
            bool anyPlayerOutside = false;

            while (players.Next(out EntityRef _, out PlayerLink _, out Transform3D playerTransform))
            {
                if (IsInsideFootprint(playerTransform.Position, min, max))
                    continue;

                anyPlayerOutside = true;
                break; // first player out of the lobby starts the run for everyone
            }

            // An EMPTY world can never satisfy this (nobody outside means nobody at all), which is
            // what keeps the run from starting before anyone has joined - the old "nobody is inside"
            // form was satisfied by an empty world just as well as a departed one, and the slot loop
            // above skips a player whose RuntimePlayer hasn't replicated yet rather than holding (the
            // same continue-instead-of-hold hole that broke TalentGateSystem). Deliberately scanned
            // off real PlayerLink entities rather than joined player slots: that also keeps working
            // for a player entity placed directly in a scene for testing, which never goes through
            // PlayerSpawnUtility.Spawn and so has no RuntimePlayer slot to count (see that utility's
            // own comment).
            if (anyPlayerOutside == false)
                return;

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
