using Quantum;
using UnityEngine;

namespace QuantumUser.View.Util
{
    public static class QuantumHelper
    {
        private static QuantumRunnerLocalDebug _localDebug;

        public static bool IsLocalPlayer(PlayerRef playerRef)
        {
            var localGame = MatchMakingConfig.I == null;

            if (localGame)
            {
                // Couch co-op: QuantumRunnerLocalDebug.LocalPlayers[] is added as players 1..N
                // (see its OnGameStarted loop), so any index within that range is locally owned -
                // not just index 1.
                if (_localDebug == null)
                    _localDebug = Object.FindFirstObjectByType<QuantumRunnerLocalDebug>();

                int localCount = _localDebug != null && _localDebug.LocalPlayers != null ? _localDebug.LocalPlayers.Length : 1;
                return playerRef._index >= 1 && playerRef._index <= localCount;
            }
            else
            {
                return MatchMakingConfig.I != null && QuantumRunner.Default.Game.PlayerIsLocal(playerRef);
            }
        }
    }
}
