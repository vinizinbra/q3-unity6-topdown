
using System;
using NaughtyAttributes;
using QuantumUser.View;
using QuantumUser.View.Managers;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    [System.Serializable]
    public class CharView : CustomQuantumEntityViewComponent
    {
        public bool isLocalPlayer;
        public Transform viewTransform;
        public QuantumGame Game => _game;
        public EntityRef EntityRef => _entityRef;
        public bool isBot = false;

        public PlayerRef PlayerRef => _playerRef;

        [Header("Ground Check")]
        [SerializeField, Tooltip("Real Unity Physics raycast, checked once here so every view component that cares (e.g. RunDustFxView) can just read LocalIsGrounded instead of raycasting independently - works for any CharView, not just the local player, despite the name (see LocalIsGrounded).")]
        private UnityEngine.LayerMask groundLayer;
        [SerializeField, Tooltip("Start the downward raycast this far above the character, in case its own collider overlaps the ground.")]
        private float groundCheckRaycastHeight = 0.5f;
        [SerializeField, Tooltip("How far below groundCheckRaycastHeight still counts as grounded.")]
        private float groundCheckDistance = 0.6f;

        // "Local" as in locally/physically checked on this client's own Unity ground collider via
        // Physics.Raycast - not "local player only". Every CharView (local player, remote players,
        // bots) computes and exposes its own, same reasoning as EnemyAttackVisualsView.SnapToGround:
        // a view-layer ground truth, independent of the simulation's KCC.Data.IsGrounded.
        public bool LocalIsGrounded { get; private set; }

        public override void Awake()
        {
            base.Awake();
            viewTransform = transform;

        }

        public override void Initialize(QuantumGame game)
        {
            base.Initialize(game);
            EntityViewManager.Instance.AddView(_playerRef,_entityRef, this, "PlayerName");
            CharacterUiWidgetManager.Instance?.SpawnWidget(_entityRef, game, transform, ResolveHeroName(game));

            if (QuantumHelper.IsLocalPlayer(_playerRef))
            {
                isLocalPlayer = true;
                MyLocalPlayer.Instance.Register(_entityRef, _playerRef, this);
            }
        }

        public override void DeInitialize(QuantumGame game)
        {
            CharacterUiWidgetManager.Instance?.DespawnWidget(_entityRef);
            MyLocalPlayer.Instance.Deinitialize(_entityRef);
            base.DeInitialize(game);
            EntityViewManager.Instance.RemoveView(_entityRef);
        }

        protected override void QUpdate(QuantumGame game)
        {
            Vector3 origin = viewTransform.position + Vector3.up * groundCheckRaycastHeight;
            LocalIsGrounded = Physics.Raycast(origin, Vector3.down, groundCheckRaycastHeight + groundCheckDistance, groundLayer);
        }

        // CharacterData asset names double as the hero's display name (Max, Lux, Zara, ...) - same
        // convention EnemyView follows for enemy names, so neither side needs its own DisplayName field.
        private string ResolveHeroName(QuantumGame game)
        {
            Frame frame = game.Frames.Predicted;
            if (frame.TryGet<CharacterStats>(_entityRef, out var stats) == false)
                return null;

            CharacterData data = frame.FindAsset(stats.CharacterData);
            return data != null ? data.name : null;
        }
    }
}
