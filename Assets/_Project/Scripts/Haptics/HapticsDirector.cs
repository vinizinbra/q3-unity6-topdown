using Quantum;
using QuantumUser.View;
using UnityEngine;

/// <summary>
/// Plays every haptic that isn't attached to a sound (those live on SoundData.haptic). Self-installed at startup and
/// kept across scenes, so there is nothing to place in a scene and no per-button / per-system code:
///
/// - UI: any press or submit on an interactable click target (Button, Toggle, tabs, cards...), via UiPressDetector.
/// - Gameplay: a fixed set of Quantum events, filtered to the local player where it's about "you". Which preset each
///   one plays (or None to switch it off) is HapticsConfig.
/// </summary>
public class HapticsDirector : MonoBehaviour
{
    private static HapticsDirector instance;

    private HapticsConfig config;

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

        UiPressDetector.Pressed += OnUiPressed;

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

    private void OnDestroy()
    {
        UiPressDetector.Pressed -= OnUiPressed;
        QuantumEvent.UnsubscribeListener(this);
    }

    // ------------------------------------------------------------------ UI

    private void OnUiPressed(GameObject target) => Haptics.Play(config.uiPress);

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
