using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Quantum;
using Quantum.Demo;
using QuantumUser.View.Util;
using UnityEngine;
using UnityEngine.Serialization;

public class PlayerManager : MonoBehaviour
{
    [System.Serializable]
    public class PlayerView
    {
        public PlayerRef playerRef;
        public EntityRef entityRef;
        public Transform playerTransform;
        public QuantumGame game;


        public PlayerView(PlayerRef playerRef, EntityRef entityRef, QuantumGame game,Transform playerTransform)
        {
            this.playerRef = playerRef;
            this.entityRef = entityRef;
            this.game = game;
            this.playerTransform = playerTransform;
        }
        
    }

    public List<PlayerView> orderedPlayersInGame = new List<PlayerView>();
    public Dictionary<EntityRef,PlayerView> PlayersInGame = new Dictionary<EntityRef, PlayerView>();

    private QuantumGame _quantumGame = null;

    public static PlayerManager Instance;
    public Action onAllPlayersConnected;
    
    void Start()
    {
        //MyLocalPlayer.Instance.onLocalPlayerSetup += OnLocalPlayerSetup;
    }

    private void OnLocalPlayerSetup(EntityRef entityRef)
    {
        _quantumGame = QuantumRunner.Default.Game;
    }

    public void AddPlayer(PlayerRef playerRef, EntityRef entityRef,Transform playerTransform)
    {
        _quantumGame = QuantumRunner.Default.Game;
        PlayersInGame.Add(entityRef,new PlayerView(playerRef, entityRef,_quantumGame,playerTransform));
        if (_quantumGame != null)
        {

            if (PlayersInGame.Count == _quantumGame.Frames.Verified.PlayerCount)
            {
                LogHelper.Warn("PlayerManager", "ON ALL PLAYERS CONNECTED");
                orderedPlayersInGame = PlayersInGame.Values.ToList();
                onAllPlayersConnected?.Invoke();
            }
        }
    }

    private float _sortTimer;
    private const float SortInterval = 0.25f;


}
