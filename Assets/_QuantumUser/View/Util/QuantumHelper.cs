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

        // Resolves playerRef to the LOCAL slot index (0-based, matching the order MatchMakingConfig.
        // StartRunner called AddPlayer for THIS client's own local players - 0/1 for couch co-op) it
        // occupies on this client, or -1 if it isn't local at all. playerRef._index is a GLOBAL,
        // room-wide index (reflects join order across every client), not a local slot - reusing it
        // directly as a local slot only happens to work for whichever client joined first. Every
        // other client's own local player(s) would resolve to the wrong slot (or none), breaking
        // anything keyed off MyLocalPlayer's slot-0 shortcuts for them specifically.
        public static int GetLocalSlotIndex(PlayerRef playerRef)
        {
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
                return playerRef._index >= 1 && playerRef._index <= localCount ? playerRef._index - 1 : -1;
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
                if (localPlayers[i] == playerRef)
                    return localSlots[i];
            }

            return -1;
        }
    }
}
