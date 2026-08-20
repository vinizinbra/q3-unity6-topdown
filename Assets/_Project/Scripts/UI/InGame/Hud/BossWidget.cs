using Photon.Deterministic;
using Quantum;
using QuantumUser.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Dedicated top-screen boss-fight HUD, shown only while Global.HudBanner == HudBannerKind.Boss -
// takes over as the boss's only HP/shield display. EnemyView skips EnemyUiWidgetManager.SpawnWidget
// entirely for EnemyTier.Boss (see its own comment), so the boss gets no floating CharacterUiWidget
// of its own; DirectorTimelineUiWidget/TraversalChallengeWidget both read that exact same shared
// HudBanner value (resolved once a tick by CombatDirectorSystem.ApplyHudBanner - see GameState.qtn's
// own HudBannerKind comment), so all three stay mutually exclusive across the whole match without
// each independently re-deriving "am I the one that should show."
//
// Single shared instance for the whole HUD (not per-local-player-slot) - same "always exists,
// self-governs visibility" shape BreathingCountdownWidget/DirectorTimelineUiWidget already use.
// Polls Global.CurrentState every QUpdate rather than subscribing to the GameStateChanged event -
// no View code reacts to that event yet, by explicit request (see CLAUDE.md's Game State section),
// and this keeps that true.
//
// Also triggers the full-screen BossWindow reveal (see BossWindow.cs) the instant the boss entity
// is first found after GameState.Boss begins - piggybacked here rather than a separate trigger
// component specifically to reuse this class's own already-running "find the boss, resolve its
// EnemyDataAsset" lookup instead of duplicating it. Fires exactly once per encounter (edge-detected
// via _wasBoss), not every tick.
//
// Also drives the camera-focus cutaway around that same reveal (confirmed with the user): fade to
// black -> snap FollowCamera onto the boss (hidden behind the fade, so no visible pan) -> fade back
// in, showing the boss in focus while BossWindow plays over it. Reversed the same way once
// Global.BossPauseTimer (see RunPhaseUtility.BeginBossEncounter/GameState.qtn) counts down to 0 -
// fade out -> clear the focus override, resuming normal multi-player framing -> fade in, right as
// GameplaySystemGroup re-enables and the fight actually becomes playable.
public class BossWidget : QuantumGlobalMonoBehaviour
{
    [SerializeField, Tooltip("Container for the widget's visible children - toggled off outside GameState.Boss. Must be a CHILD GameObject, not the GameObject this script itself lives on, since QUpdate stops firing once its own GameObject is disabled.")]
    private GameObject root;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private Slider healthSlider;
    [SerializeField, Tooltip("Shows \"current/max\" (rounded to whole numbers) alongside healthSlider.")]
    private TMP_Text healthText;
    [SerializeField, Tooltip("Shown only while the boss carries a Shield component with a Max above zero - e.g. GrasslandOutpostBoss (ShieldMultiplier = 1). A boss with no shield authored just never shows this.")]
    private Slider shieldSlider;
    [SerializeField, Tooltip("Shows \"current/max\" (rounded to whole numbers) alongside shieldSlider - shown/hidden together with it.")]
    private TMP_Text shieldText;
    [SerializeField, Tooltip("Shown once, the instant the boss entity is first found this encounter - populated from its own EnemyDataAsset if it's a BossDataAsset (Title/Subtitle/UiSprite), left as whatever's already on the prefab otherwise.")]
    private BossWindow bossWindow;
    [SerializeField, Tooltip("Duration of each fade-out/fade-in half of the camera-focus cutaway (see ScreenFadeWidget). Falls back to an instant camera snap with no fade at all if ScreenFadeWidget.Instance isn't found in the scene.")]
    private float cameraFadeDuration = 0.25f;

    private QuantumEntityViewUpdater _entityViewUpdater;
    private bool _wasBoss;
    private bool _wasPaused;

    private void Awake()
    {
        _entityViewUpdater = FindFirstObjectByType<QuantumEntityViewUpdater>();
    }

    public override void QStart(QuantumGame game)
    {
    }

    public override void QLateUpdate(QuantumGame game)
    {
    }

    public override unsafe void QUpdate(QuantumGame game)
    {
        Frame frame = game.Frames.Predicted;
        bool isBoss = frame.Global->HudBanner == HudBannerKind.Boss;

        SetShown(root, isBoss);

        if (isBoss == false)
        {
            _wasBoss = false;
            _wasPaused = false;
            return;
        }

        var bosses = frame.Filter<BossRuntimeState, Health>();
        if (bosses.Next(out EntityRef bossEntity, out _, out Health health) == false)
            return;

        UpdateHealth(health);
        UpdateShield(frame, bossEntity);
        UpdateName(frame, bossEntity);

        if (_wasBoss == false)
            TriggerBossWindow(frame, bossEntity);

        _wasBoss = true;

        bool isPaused = frame.Global->BossPauseTimer > FP._0;

        if (_wasPaused == true && isPaused == false)
            ReturnCameraFocusToPlayers();

        _wasPaused = isPaused;
    }

    private void TriggerBossWindow(Frame frame, EntityRef bossEntity)
    {
        bool hasWindow = bossWindow != null;
        bool hasEnemy = frame.TryGet<Enemy>(bossEntity, out var enemy);
        Debug.Log($"[BossWidget] TriggerBossWindow: hasWindow={hasWindow}, hasEnemy={hasEnemy}");

        if (hasWindow == false || hasEnemy == false)
            return;

        EnemyDataAsset data = frame.FindAsset(enemy.EnemyData);
        BossDataAsset bossData = data as BossDataAsset;

        Transform bossTransform = ResolveViewTransform(bossEntity);

        if (ScreenFadeWidget.Instance == null)
        {
            FollowCamera.I?.SetFocusOverride(bossTransform);
            ShowBossWindow(bossData);
            return;
        }

        ScreenFadeWidget.Instance.FadeOut(cameraFadeDuration, onComplete: () =>
        {
            FollowCamera.I?.SetFocusOverride(bossTransform);
            ShowBossWindow(bossData);
            ScreenFadeWidget.Instance.FadeIn(cameraFadeDuration);
        });
    }

    private void ShowBossWindow(BossDataAsset bossData)
    {
        if (bossData != null)
            bossWindow.SetContent(bossData.Title, bossData.Subtitle, bossData.UiSprite);

        bossWindow.Show();
    }

    // No fade needed on the way back, unlike TriggerBossWindow's own cut TO the boss - confirmed
    // with the user. snap: false so FollowCamera's own existing Update() lerp eases it back to the
    // players naturally instead of popping instantly; the camera was already framing the arena
    // (players/boss are both right there), so there's nothing jarring here to hide behind a fade.
    private void ReturnCameraFocusToPlayers()
    {
        FollowCamera.I?.ClearFocusOverride(snap: false);
    }

    private Transform ResolveViewTransform(EntityRef entity)
    {
        if (_entityViewUpdater == null)
            return null;

        QuantumEntityView view = _entityViewUpdater.GetView(entity);
        return view != null ? view.transform : null;
    }


    private void UpdateHealth(Health health)
    {
        if (health.MaxHealth <= FP._0)
            return;

        SetSliderValue(healthSlider, (health.CurrentHealth / health.MaxHealth).AsFloat);

        if (healthText != null)
            healthText.text = $"{Mathf.RoundToInt(health.CurrentHealth.AsFloat)}/{Mathf.RoundToInt(health.MaxHealth.AsFloat)}";
    }

    private void UpdateShield(Frame frame, EntityRef bossEntity)
    {
        bool shown = frame.TryGet<Shield>(bossEntity, out var shield) && shield.Max > FP._0;

        SetShown(shieldSlider, shown);
        SetShown(shieldText, shown);

        if (shown == false)
            return;

        SetSliderValue(shieldSlider, (shield.Current / shield.Max).AsFloat);

        if (shieldText != null)
            shieldText.text = $"{Mathf.RoundToInt(shield.Current.AsFloat)}/{Mathf.RoundToInt(shield.Max.AsFloat)}";
    }

    private void UpdateName(Frame frame, EntityRef bossEntity)
    {
        if (nameText == null || frame.TryGet<Enemy>(bossEntity, out var enemy) == false)
            return;

        EnemyDataAsset data = frame.FindAsset(enemy.EnemyData);
        if (data == null)
            return;

        nameText.text = string.IsNullOrEmpty(data.EnemyName) ? data.name : data.EnemyName;
    }

    private static void SetSliderValue(Slider slider, float value)
    {
        if (slider == null)
            return;

        slider.value = value;
    }

    private static void SetShown(GameObject go, bool shown)
    {
        if (go == null || go.activeSelf == shown)
            return;

        go.SetActive(shown);
    }

    private static void SetShown(Component component, bool shown)
    {
        if (component == null)
            return;

        SetShown(component.gameObject, shown);
    }
}
