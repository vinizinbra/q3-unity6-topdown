namespace Quantum
{
    using UnityEngine.Scripting;

    // Minimal, vocabulary-only run-failure hook (see docs/revive.md/GameState.qtn) - fires once
    // when every connected, spawned player is simultaneously not-Alive AND nobody has any way back
    // on their own: KO is a dead end (no teammate revive, no self-revive - see
    // PlayerLifeStateUtility.EnterKO/ReviveUtility.TryPerformSelfRevive) until
    // Global.BreathingAreaSecured, so a KO'd player's own SelfReviveCharges (if any remain unspent)
    // no longer count as an escape; only a still-Downed player's charges do. Same "wired later"
    // precedent GameState.Event/pre-2026-08-17 GameState.Boss already established - nothing
    // downstream consumes GameState.RunFailed yet.
    [Preserve]
    public unsafe class RunFailureSystem : SystemMainThread
    {
        public override void Update(Frame f)
        {
            if (f.Global->CurrentState == GameState.RunFailed)
                return;

            for (int i = 0; i < f.MaxPlayerCount; i++)
            {
                PlayerRef player = i;

                if (f.GetPlayerData(player) == null)
                    continue;

                if (PlayerSpawnUtility.HasSpawned(f, player) == false)
                    return;
            }

            var players = f.Filter<PlayerLink, PlayerLifeState, CharacterStats>();
            bool anyPlayer = false;

            while (players.Next(out EntityRef _, out PlayerLink _, out PlayerLifeState lifeState, out CharacterStats stats))
            {
                anyPlayer = true;

                if (lifeState.State == PlayerLifeStateKind.Alive)
                    return;

                if (lifeState.State == PlayerLifeStateKind.Downed && stats.SelfReviveCharges > 0)
                    return;
            }

            if (anyPlayer == false)
                return;

            GameStateUtility.SetState(f, GameState.RunFailed);
        }
    }
}
