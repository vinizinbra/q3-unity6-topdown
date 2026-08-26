namespace Quantum
{
    // The single place anything asks "what is this player pressing this tick".
    //
    // A real player's Input arrives through the deterministic input stream (f.GetPlayerInput); a
    // bot's is synthesized by BotInputSystem into its own BotBrain.Data earlier in the same tick
    // (see BotBrain.qtn / docs/bots.md). Both are the same Input struct, so every consumer keeps
    // reading Direction/Run/Fire/DashSkill/HeroSkill exactly as before and none of them needs to
    // know a bot exists - swapping f.GetPlayerInput for this call is the whole integration.
    //
    // Deliberately keyed off the BotBrain COMPONENT rather than RuntimePlayer.IsBot: the flag is
    // read once at spawn (PlayerSpawnUtility) and turned into a component, so this - which runs
    // several times per player per tick - is a plain component lookup, not a RuntimePlayer fetch.
    public static unsafe class PlayerInputUtility
    {
        public static Input* Resolve(Frame f, EntityRef entity, PlayerLink* playerLink)
        {
            if (f.Unsafe.TryGetPointer<BotBrain>(entity, out var brain) == true)
                return &brain->Data;

            return f.GetPlayerInput(playerLink->Player);
        }

        // True for a simulation-driven player. Same component check Resolve makes - exposed so the
        // handful of "don't hold the humans up waiting for this player" gates (LevelUpSystem's own
        // per-player confirm, RunPhaseUtility's Breathing skip vote) can read it without also
        // resolving an Input they don't want.
        public static bool IsBot(Frame f, EntityRef entity)
        {
            return f.Has<BotBrain>(entity);
        }
    }
}
