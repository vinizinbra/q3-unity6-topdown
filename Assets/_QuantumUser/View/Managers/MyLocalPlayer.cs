using System;
using Quantum;
using UnityEngine;
using Object = System.Object;

namespace QuantumUser.View
{
    public class MyLocalPlayer : QuantumGlobalMonoBehaviour
    {
        private EntityRef _entityRef = default;
        private PlayerRef _playerRef = default;
        public CharView _localPlayerView;
        private bool _isLocalPlayerSetup = false;
        public QuantumDebugInput quantumInput;
        public bool isLocalPlayerDead;
        private QuantumGame _game = null;

        public static MyLocalPlayer Instance;
        public Action<EntityRef> onLocalPlayerSetup;
        public EntityRef EntityRef => _entityRef;
        public PlayerRef PlayerRef => _playerRef;
        public bool IsLocalPlayerSetup => _isLocalPlayerSetup;
        public float timeDead = 0;

        public void Awake()
        {
            Instance = this;
        
        }
        public void Setup(EntityRef entityRef, PlayerRef playerRef, CharView localPlayerView)
        {
            _entityRef = entityRef;
            _playerRef = playerRef;
            _game = QuantumRunner.Default.Game;
            _localPlayerView = localPlayerView;
            onLocalPlayerSetup?.Invoke(entityRef);
            _isLocalPlayerSetup = true;
            FollowCamera.I.AssignCamera(_localPlayerView);
        }

        public void Deinitialize(EntityRef entityRef)
        {
            if (entityRef == _entityRef)
            {
                _isLocalPlayerSetup = false;
                _entityRef = default;
                _playerRef = default;
            }
        }

        public void AddOnLocalPlayerSetup(Action<EntityRef> action)
        {
            onLocalPlayerSetup += action;
        
            if(_isLocalPlayerSetup)
                action?.Invoke(_entityRef);
        }


        public override void QStart(QuantumGame game)
        {
            quantumInput = UnityEngine.Object.FindFirstObjectByType<QuantumDebugInput>();
        }

        public override void QUpdate(QuantumGame game)
        {
        }

        public override void QLateUpdate(QuantumGame game)
        {
        }
    }
}
