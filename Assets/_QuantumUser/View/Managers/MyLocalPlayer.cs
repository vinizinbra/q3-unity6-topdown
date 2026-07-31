using System;
using System.Collections.Generic;
using Quantum;
using UnityEngine;

namespace QuantumUser.View
{
    public struct LocalPlayerSlot
    {
        public EntityRef EntityRef;
        public PlayerRef PlayerRef;
        public CharView View;
        public bool IsSet;
    }

    public class MyLocalPlayer : QuantumGlobalMonoBehaviour
    {
        // Couch co-op is capped at 2 - matches QuantumDebugInput's two hardcoded input schemes.
        private const int MaxLocalPlayers = 2;

        private readonly LocalPlayerSlot[] _slots = new LocalPlayerSlot[MaxLocalPlayers];
        public QuantumDebugInput quantumInput;
        public bool isLocalPlayerDead;
        private QuantumGame _game = null;

        public static MyLocalPlayer Instance;
        public Action<EntityRef> onLocalPlayerSetup;
        public Action<EntityRef, int> onLocalPlayerRegistered;
        public Action<EntityRef, int> onLocalPlayerUnregistered;
        public IReadOnlyList<LocalPlayerSlot> Slots => _slots;

        // Slot-0 accessors for callers that only ever care about "a" local player (debug tools).
        public EntityRef EntityRef => _slots[0].EntityRef;
        public PlayerRef PlayerRef => _slots[0].PlayerRef;
        public bool IsLocalPlayerSetup => _slots[0].IsSet;
        public CharView _localPlayerView => _slots[0].View;

        public bool AnyLocalPlayerSetup
        {
            get
            {
                foreach (var slot in _slots)
                    if (slot.IsSet)
                        return true;
                return false;
            }
        }
        public float timeDead = 0;

        public void Awake()
        {
            Instance = this;

        }

        public void Register(EntityRef entityRef, PlayerRef playerRef, CharView view)
        {
            int slotIndex = playerRef._index - 1;
            if (slotIndex < 0 || slotIndex >= MaxLocalPlayers)
                return;

            _slots[slotIndex] = new LocalPlayerSlot { EntityRef = entityRef, PlayerRef = playerRef, View = view, IsSet = true };
            _game = QuantumRunner.Default.Game;
            FollowCamera.I.AddTarget(view.viewTransform);
            onLocalPlayerSetup?.Invoke(entityRef);
            onLocalPlayerRegistered?.Invoke(entityRef, slotIndex);
        }

        public void Deinitialize(EntityRef entityRef)
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i].IsSet == false || _slots[i].EntityRef != entityRef)
                    continue;

                FollowCamera.I.RemoveTarget(_slots[i].View.viewTransform);
                _slots[i] = default;
                onLocalPlayerUnregistered?.Invoke(entityRef, i);
            }
        }

        public bool IsLocalEntity(EntityRef entityRef)
        {
            foreach (var slot in _slots)
                if (slot.IsSet && slot.EntityRef == entityRef)
                    return true;

            return false;
        }

        public void AddOnLocalPlayerSetup(Action<EntityRef> action)
        {
            onLocalPlayerSetup += action;

            foreach (var slot in _slots)
                if (slot.IsSet)
                    action?.Invoke(slot.EntityRef);
        }

        // Same idea as AddOnLocalPlayerSetup, but for HUD elements that must only ever track one
        // specific local slot (e.g. the player's own skill HUD staying "player 1 only" even when a
        // second local player joins for couch co-op) rather than re-binding to whichever local
        // player registered most recently.
        public void BindToSlot(int slotIndex, Action<EntityRef> onBound)
        {
            onLocalPlayerRegistered += (entityRef, registeredSlotIndex) =>
            {
                if (registeredSlotIndex == slotIndex)
                    onBound(entityRef);
            };

            if (slotIndex >= 0 && slotIndex < MaxLocalPlayers && _slots[slotIndex].IsSet)
                onBound(_slots[slotIndex].EntityRef);
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
