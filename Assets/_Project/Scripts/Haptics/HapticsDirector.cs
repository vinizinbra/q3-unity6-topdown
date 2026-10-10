using System.Collections.Generic;
using Quantum;
using QuantumUser.View;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Plays every haptic that isn't attached to a sound (those live on SoundData.haptic). Self-installed at startup and
/// kept across scenes, so there is nothing to place in a scene and no per-button / per-system code:
///
/// - UI: any press or submit on an interactable click target (Button, Toggle, tabs, cards...) - found by raycasting
///   the EventSystem the same way a click would, so it follows whatever the UI already does. Control Freak 2's touch
///   panel only handles pointer down/up (never click), so the gameplay joystick/buttons don't buzz.
/// - Gameplay: a fixed set of Quantum events, filtered to the local player where it's about "you". Which preset each
///   one plays (or None to switch it off) is HapticsConfig.
/// </summary>
public class HapticsDirector : MonoBehaviour
{
    private static HapticsDirector instance;

    private readonly List<RaycastResult> raycastResults = new();
    private PointerEventData pointerData;
    private HapticsConfig config;
    private GameObject submitTarget;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        if (instance != null)
            return;

        var go = new GameObject(nameof(HapticsDirector));
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        instance = go.AddComponent<HapticsDirector>();
    }

    private void Awake()
    {
        config = HapticsConfig.Instance;

        QuantumEvent.Subscribe<EventEntityDamaged>(this, OnEntityDamaged);
        QuantumEvent.Subscribe<EventShieldBroken>(this, e => PlayIfLocal(e.Target, config.shieldBroken));
        QuantumEvent.Subscribe<EventAccessoryBlocked>(this, e => PlayIfLocal(e.Owner, config.accessoryBlocked));
        QuantumEvent.Subscribe<EventAccessoryBroken>(this, e => PlayIfLocal(e.Owner, config.accessoryBroken));
        QuantumEvent.Subscribe<EventPlayerDowned>(this, e => PlayIfLocal(e.Entity, config.downed));
        QuantumEvent.Subscribe<EventPlayerKO>(this, e => PlayIfLocal(e.Entity, config.knockedOut));
        QuantumEvent.Subscribe<EventPlayerRevived>(this, OnPlayerRevived);
        QuantumEvent.Subscribe<EventSkillActionBeginExecuted>(this, e => PlayIfLocal(e.Entity, config.skillUsed));
        QuantumEvent.Subscribe<EventEntityDied>(this, OnEntityDied);
        QuantumEvent.Subscribe<EventGroundbreakerSlammed>(this, e => PlayIfLocal(e.Owner, config.impactSlam));
        QuantumEvent.Subscribe<EventWallSlammed>(this, e => PlayIfLocal(e.Owner, config.impactSlam));
        QuantumEvent.Subscribe<EventGameStateChanged>(this, OnGameStateChanged);
    }

    private void OnDestroy() => QuantumEvent.UnsubscribeListener(this);

    // ------------------------------------------------------------------ UI

    private void Update()
    {
        if (Haptics.Enabled == false)
            return;

        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
            return;

        if (TryGetPressPosition(out Vector2 position))
        {
            if (IsClickTargetAt(eventSystem, position))
                Haptics.Play(config.uiPress);
        }
        else if (SubmitPressed(eventSystem) && submitTarget != null)
        {
            Haptics.Play(config.uiPress);
        }
    }

    // Submit acts on the press frame itself, inside EventSystem.Update - which may run before this Update and has
    // already clicked the button by then (an upgrade card disables itself the moment it is picked). So the target
    // is what was selected and pressable at the end of the previous frame.
    private void LateUpdate()
    {
        EventSystem eventSystem = EventSystem.current;
        GameObject handler = eventSystem != null ? ExecuteEvents.GetEventHandler<ISubmitHandler>(eventSystem.currentSelectedGameObject) : null;
        submitTarget = IsInteractable(handler) ? handler : null;
    }

    // Every touch, not just Pointer.current's primary one: mid-match the first finger is usually on the joystick
    // and the button press is the second.
    private static bool TryGetPressPosition(out Vector2 position)
    {
        Touchscreen touchscreen = Touchscreen.current;
        if (touchscreen != null)
        {
            foreach (var touch in touchscreen.touches)
            {
                if (touch.press.wasPressedThisFrame)
                {
                    position = touch.position.ReadValue();
                    return true;
                }
            }
        }

        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            position = mouse.position.ReadValue();
            return true;
        }

        position = default;
        return false;
    }

    private static bool SubmitPressed(EventSystem eventSystem)
    {
        var module = eventSystem.currentInputModule as InputSystemUIInputModule;
        InputAction submit = module != null && module.submit != null ? module.submit.action : null;
        return submit != null && submit.WasPressedThisFrame();
    }

    // Topmost hit only, like the click itself: a button under a popup's dim background isn't what was pressed.
    private bool IsClickTargetAt(EventSystem eventSystem, Vector2 position)
    {
        pointerData ??= new PointerEventData(eventSystem);
        pointerData.Reset();
        pointerData.position = position;

        raycastResults.Clear();
        eventSystem.RaycastAll(pointerData, raycastResults);
        if (raycastResults.Count == 0)
            return false;

        return IsInteractable(ExecuteEvents.GetEventHandler<IPointerClickHandler>(raycastResults[0].gameObject));
    }

    // A handler without a Selectable (a custom card) counts; a disabled button doesn't.
    private static bool IsInteractable(GameObject handler)
    {
        if (handler == null)
            return false;

        return handler.TryGetComponent(out Selectable selectable) == false || selectable.IsInteractable();
    }

    // ------------------------------------------------------------------ gameplay

    private void OnEntityDamaged(EventEntityDamaged e)
    {
        if (e.Silent || e.Damage <= 0)
            return;

        if (IsLocal(e.Target))
            Haptics.Play(config.damageTaken);
        else if (e.IsCritical && IsLocal(e.Owner))
            Haptics.Play(config.criticalHit);
    }

    private void OnPlayerRevived(EventPlayerRevived e)
    {
        if (IsLocal(e.Target) || IsLocal(e.Reviver))
            Haptics.Play(config.revived);
    }

    // A dead enemy lingers (Phase == Dead) for its death animation, so its data is still readable here.
    private void OnEntityDied(EventEntityDied e)
    {
        Frame frame = e.Game.Frames.Predicted;
        if (frame == null || frame.TryGet<Enemy>(e.Target, out var enemy) == false)
            return;

        EnemyDataAsset data = frame.FindAsset(enemy.EnemyData);
        if (data == null)
            return;

        if (data.Tier == EnemyTier.Boss)
            Haptics.Play(config.bossKilled);
        else if (data.Tier == EnemyTier.Elite && IsLocal(e.Owner))
            Haptics.Play(config.eliteKilled);
    }

    private void OnGameStateChanged(EventGameStateChanged e)
    {
        // Closing an upgrade screen returns to the state it interrupted - re-entering Boss that way is not a reveal.
        if (e.PreviousState == GameState.Upgrade)
            return;

        HapticCue cue = e.NewState switch
        {
            GameState.Upgrade => config.upgradeScreen,
            GameState.Boss => config.bossReveal,
            GameState.Victory => config.victory,
            GameState.RunFailed => config.runFailed,
            _ => null,
        };

        Haptics.Play(cue);
    }

    private static void PlayIfLocal(EntityRef entity, HapticCue cue)
    {
        if (IsLocal(entity))
            Haptics.Play(cue);
    }

    // Couch co-op: every local slot counts. Bots never do (they don't register with MyLocalPlayer).
    private static bool IsLocal(EntityRef entity)
    {
        if (MyLocalPlayer.Instance == null || MyLocalPlayer.Instance.AnyLocalPlayerSetup == false)
            return false;

        return MyLocalPlayer.Instance.IsLocalEntity(entity);
    }
}
