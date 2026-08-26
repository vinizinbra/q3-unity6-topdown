using Quantum;
using UnityEngine;

namespace QuantumUser.View.Util
{
    public static class QuantumHelper
    {
        private static QuantumRunnerLocalDebug _localDebug;

        public static bool IsLocalPlayer(PlayerRef playerRef)
        {
            return GetLocalSlotIndex(playerRef) >= 0;
        }

        // True for a simulation-driven bot player (RuntimePlayer.IsBot - see docs/bots.md). A bot
        // occupies a real player slot, and on a local-debug session it is literally one of THIS
        // client's own local players, so without this every "is this mine" View path (camera
        // targets, HUD binding, couch-co-op slot assignment) would happily adopt it.
        public static bool IsBotPlayer(PlayerRef playerRef)
        {
            QuantumGame game = QuantumRunner.Default != null ? QuantumRunner.Default.Game : null;

            if (game == null)
                return false;

            Frame frame = game.Frames.Predicted;
            RuntimePlayer playerData = frame != null ? frame.GetPlayerData(playerRef) : null;

            return playerData != null && playerData.IsBot;
        }

        // Resolves playerRef to the LOCAL slot index (0-based, matching the order MatchMakingConfig.
        // StartRunner called AddPlayer for THIS client's own local players - 0/1 for couch co-op) it
        // occupies on this client, or -1 if it isn't local at all. playerRef._index is a GLOBAL,
        // room-wide index (reflects join order across every client), not a local slot - reusing it
        // directly as a local slot only happens to work for whichever client joined first. Every
        // other client's own local player(s) would resolve to the wrong slot (or none), breaking
        // anything keyed off MyLocalPlayer's slot-0 shortcuts for them specifically.
        //
        // BOTS ARE NOT LOCAL PLAYERS (see docs/bots.md). A bot always resolves to -1, and - just as
        // importantly - never CONSUMES a slot: the index returned counts only the non-bot local
        // players ahead of it, so a human sitting behind two bots in LocalPlayers[] is still slot
        // 0 and still gets the camera, the skill HUD and the Choice Window. This one exclusion is
        // what keeps every other "is available to the local player" call site in the project
        // bot-unaware.
        public static int GetLocalSlotIndex(PlayerRef playerRef)
        {
            if (IsBotPlayer(playerRef) == true)
                return -1;

            var localGame = MatchMakingConfig.I == null;

            if (localGame)
            {
                // Couch co-op debug (single machine, no network): QuantumRunnerLocalDebug.
                // LocalPlayers[] is added as players 1..N (see its OnGameStarted loop), so - unlike
                // the real networked path below - there's no separate room-wide numbering to
                // reconcile: the global index directly IS the local slot.
                if (_localDebug == null)
                    _localDebug = Object.FindFirstObjectByType<QuantumRunnerLocalDebug>();

                int localCount = _localDebug != null && _localDebug.LocalPlayers != null ? _localDebug.LocalPlayers.Length : 1;

                if (playerRef._index < 1 || playerRef._index > localCount)
                    return -1;

                return playerRef._index - 1 - CountBotsBefore(playerRef._index - 1);
            }

            if (QuantumRunner.Default == null)
                return -1;

            // GetLocalPlayers()/GetLocalPlayerSlots() are parallel arrays (Quantum SDK) - the global
            // PlayerRef this client controls at index i corresponds to the local slot at the same
            // index i, regardless of what that global PlayerRef's own room-wide number happens to be.
            QuantumGame game = QuantumRunner.Default.Game;
            var localPlayers = game.GetLocalPlayers();
            var localSlots = game.GetLocalPlayerSlots();

            for (int i = 0; i < localPlayers.Count; i++)
            {
                if (localPlayers[i] != playerRef)
                    continue;

                int rawSlot = localSlots[i];
                int botsAhead = 0;

                for (int j = 0; j < localPlayers.Count; j++)
                {
                    if (localSlots[j] < rawSlot && IsBotPlayer(localPlayers[j]) == true)
                        botsAhead++;
                }

                return rawSlot - botsAhead;
            }

            return -1;
        }

        // Local-debug counterpart of the botsAhead loop above - reads QuantumRunnerLocalDebug's own
        // authored LocalPlayers[] directly rather than going through the frame, since in that mode
        // the array index IS the local slot.
        private static int CountBotsBefore(int localSlot)
        {
            if (_localDebug == null || _localDebug.LocalPlayers == null)
                return 0;

            int bots = 0;

            for (int i = 0; i < localSlot && i < _localDebug.LocalPlayers.Length; i++)
            {
                if (_localDebug.LocalPlayers[i] != null && _localDebug.LocalPlayers[i].IsBot == true)
                    bots++;
            }

            return bots;
        }
    }
}
