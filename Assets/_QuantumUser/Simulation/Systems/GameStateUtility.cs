namespace Quantum
{
    // Single place Global.CurrentState actually changes - see GameState.qtn for the full state
    // list/semantics and docs/talents.md. Deliberately thin (set + fire event only) - each state's
    // own pause behavior (or lack of it) is owned by whichever system/utility drives that specific
    // transition (LobbyBoundarySystem for Lobby->Survival, LevelUpUtility for Survival<->Upgrade),
    // not centralized here, since "does this transition also pause GameplaySystemGroup" genuinely
    // differs per state (Lobby must NOT freeze player movement, Upgrade must).
    public static unsafe class GameStateUtility
    {
        public static void SetState(Frame f, GameState newState)
        {
            GameState previous = f.Global->CurrentState;

            if (previous == newState)
                return;

            f.Global->CurrentState = newState;
            f.Events.GameStateChanged(previous, newState);
        }
    }
}
