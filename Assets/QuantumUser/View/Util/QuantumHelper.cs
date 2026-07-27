using Quantum;
using UnityEngine;

namespace QuantumUser.View.Util
{
    public static class QuantumHelper
    {
        public static bool IsLocalPlayer(PlayerRef playerRef)
        {
            var localGame = MatchMakingConfig.I == null;

            if (localGame)
            {
                return playerRef._index == 1;
            }
            else
            {
                return MatchMakingConfig.I != null && QuantumRunner.Default.Game.PlayerIsLocal(playerRef);
            }
        }
    
    }
}