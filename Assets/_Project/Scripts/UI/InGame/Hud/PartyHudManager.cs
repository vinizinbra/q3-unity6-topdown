using System.Collections.Generic;
using Quantum;
using QuantumUser.View.Managers;
using QuantumUser.View.Util;
using UnityEngine;

// Always-visible party HUD showing skill/cooldown data for every player currently in the match,
// local or remote - separate from the player's own full HUD cluster (SkillCooldownUiWidget etc.
// self-bound to local slot 0), which stays showing only player 1 regardless of couch co-op.
// The first local player (local slot 0, "player 1") always reuses defaultLocalSlot, a fixed slot
// already placed in the prefab/scene, so it behaves exactly as before this strip existed. Every
// other player - the second local co-op player and any remote player - gets a slot instantiated
// from slotPrefab under slotContainer as they join, and destroyed as they leave, since the match's
// actual player count isn't known ahead of time. slotPrefab is left as authored (not force-disabled
// here) - whatever active/inactive state it's given in the scene is up to whoever builds it.
public class PartyHudManager : MonoBehaviour
{
    [SerializeField] private PartyHudWidget defaultLocalSlot;
    [SerializeField] private PartyHudWidget slotPrefab;
    [SerializeField] private Transform slotContainer;

    private readonly Dictionary<EntityRef, PartyHudWidget> _spawnedSlots = new();

    private void Awake()
    {
    }

    private void OnEnable()
    {
        EntityViewManager.Instance.onPlayerAdded += OnPlayerAdded;
        EntityViewManager.Instance.onPlayerRemoved += OnPlayerRemoved;
    }

    private void OnDisable()
    {
        if (EntityViewManager.Instance != null)
        {
            EntityViewManager.Instance.onPlayerAdded -= OnPlayerAdded;
            EntityViewManager.Instance.onPlayerRemoved -= OnPlayerRemoved;
        }
    }

    private void OnPlayerAdded(CharView charView)
    {
        bool isFirstLocal = IsFirstLocalPlayer(charView);
        LogHelper.Log("PartyHud", $"Player added: entity={charView.EntityRef}, playerRef={charView.PlayerRef}, isFirstLocalPlayer={isFirstLocal}", this);

        if (isFirstLocal)
        {
            if (defaultLocalSlot == null)
            {
                LogHelper.Warn("PartyHud", "defaultLocalSlot is unassigned - player 1 won't show in the strip.", this);
                return;
            }

            defaultLocalSlot.gameObject.SetActive(true);
            defaultLocalSlot.Initialize(charView.EntityRef);
            return;
        }

        if (slotPrefab == null || slotContainer == null)
        {
            LogHelper.Warn("PartyHud", "slotPrefab or slotContainer is unassigned - this player won't show in the strip.", this);
            return;
        }

        var slot = Instantiate(slotPrefab, slotContainer);
        slot.gameObject.SetActive(true);
        slot.Initialize(charView.EntityRef);
        _spawnedSlots[charView.EntityRef] = slot;
    }

    private void OnPlayerRemoved(EntityRef entityRef)
    {
        if (defaultLocalSlot != null && defaultLocalSlot.BoundEntityRef == entityRef)
        {
            defaultLocalSlot.Clear();
            return;
        }

        if (_spawnedSlots.Remove(entityRef, out var slot) && slot != null)
            Destroy(slot.gameObject);
    }

    // "First local player" == local slot 0 (player 1), same convention MyLocalPlayer uses for its
    // own slots - checked via QuantumHelper directly (not CharView.isLocalPlayer/MyLocalPlayer's
    // slots) since onPlayerAdded fires from CharView.Initialize before either of those is set.
    // GetLocalSlotIndex resolves against this client's own local player list (Quantum's
    // GetLocalPlayers/GetLocalPlayerSlots), not charView.PlayerRef's raw room-wide index - a global
    // index only lines up with local slot 0 for whichever client happened to join the room first,
    // so any other client's own "player 1" would otherwise never match here and fall through to
    // the generic slotPrefab path instead of defaultLocalSlot.
    private static bool IsFirstLocalPlayer(CharView charView)
    {
        return QuantumHelper.GetLocalSlotIndex(charView.PlayerRef) == 0;
    }
}
