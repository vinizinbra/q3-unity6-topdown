using NaughtyAttributes;
using PrimeTween;
using QuantumUser.View.Managers;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Chest's own open reaction - see docs/chests.md. Purely cosmetic/View-side: the actual
    // upgrade-choice screen opening is already driven server-side off Frame.Global.
    // LevelUpScreenOpen (GameplayUiController), independent of whatever this plays.
    //
    // A two-phase sequence on ChestOpened, both phases scale-based (self-contained here, not
    // JuicyEffects):
    //   1. Shake-scale buildup (shakeBuildupDuration, e.g. 0.5s) - the chest trembles in place,
    //      still closed.
    //   2. Once that settles: punch-scale open (punchDuration, e.g. 0.3s) - and the particle burst
    //      + sprite swap to openSprite fire at the START of this phase, together with the punch,
    //      not staggered off its completion.
    // Both tweens default to unscaled time - this plays right as GameplayUiController starts
    // easing Time.timeScale down toward 0 for the upgrade screen, and a scaled-time tween would
    // slow to a crawl right along with it. The scale tweens + sprite swap live on visualRoot and
    // die with the chest entity (destroyed ~1 frame after the screen closes - see ChestSystem), but
    // they run on unscaled time during the paused screen so they've already finished by then. The
    // open particle, by contrast, is NOT a child of this chest - it's played as a pooled, external
    // one-shot via EffectsManager (openEffectPrefab), so it survives and finishes on its own
    // regardless of the chest's near-immediate destroy.
    //
    // Targets visualRoot, NOT this component's own transform - QuantumEntityView drives that
    // transform every frame from the simulation's own Transform3D and would fight a tween applied
    // directly to it (same reasoning PixieBombView's own visualRoot comment gives).
    public class ChestView : CustomQuantumEntityViewComponent
    {
        [SerializeField] private Transform visualRoot;
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private Sprite openSprite;
        [SerializeField, Tooltip("Played once at the start of the punch-open phase, together with the sprite swap - leave unassigned to skip. Played as a POOLED, EXTERNAL one-shot via EffectsManager (NOT a child of this chest), so it survives and finishes even though the chest entity is destroyed ~1 frame after the upgrade screen closes. Must be a non-looping prefab - EffectsManager releases it back to its pool once ParticleSystem.IsAlive goes false.")]
        private ParticleSystem openEffectPrefab;
        [SerializeField, Tooltip("World-space offset from the chest's position where openEffectPrefab plays.")]
        private Vector3 openEffectOffset;

        [Header("Phase 1 - shake-scale buildup")]
        [SerializeField] private Vector3 shakeBuildupStrength = new Vector3(0.08f, 0.08f, 0f);
        [SerializeField] private float shakeBuildupDuration = 0.5f;
        [SerializeField] private float shakeBuildupFrequency = 18f;
        [SerializeField, Tooltip("Amplitude envelope across phase 1 (normalized time 0->1). Rising 0->1 = a real buildup - the tremble grows from nothing to full right as the punch/explosion fires. PrimeTween's default shake does the opposite (Ease.OutQuad falloff: starts violent, settles to still), which reads as winding down instead of anticipation.")]
        private AnimationCurve shakeBuildupCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Phase 2 - punch-scale open (particle + sprite swap fire here)")]
        [SerializeField] private Vector3 punchStrength = new Vector3(0.3f, 0.3f, 0f);
        [SerializeField] private float punchDuration = 0.3f;
        [SerializeField] private float punchFrequency = 16f;

        [SerializeField, Tooltip("If true, both phases ignore Time.timeScale (run on real/unscaled time) - needed since this plays right as GameplayUiController starts easing Time.timeScale down toward 0 for the upgrade screen, and a scaled-time tween would slow to a crawl right along with it.")]
        private bool useUnscaledTime = true;

        private Vector3 _baseScale;
        private Sprite _defaultSprite;
        private Tween _buildupTween;
        private Tween _punchTween;

        public override void Awake()
        {
            base.Awake();

            if (visualRoot != null)
                _baseScale = visualRoot.localScale;

            if (spriteRenderer != null)
                _defaultSprite = spriteRenderer.sprite;

            QuantumEvent.Subscribe<EventChestOpened>(this, OnChestOpened);
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            QuantumEvent.UnsubscribeListener(this);
            _buildupTween.Stop();
            _punchTween.Stop();
        }

        private void OnChestOpened(EventChestOpened e)
        {
            if (e.Chest != _entityRef)
                return;

            PlayOpenAnimation();
        }

        // Editor-only test hook (see ResetView below to undo) - lets you preview/tune the whole
        // buildup -> punch/particle/sprite sequence directly on the prefab/scene instance without
        // needing to walk a player into an actual Chest in Play Mode.
        [Button]
        public void TestOpenAnimation()
        {
            PlayOpenAnimation();
        }

        private void PlayOpenAnimation()
        {
            _buildupTween.Stop();
            _punchTween.Stop();

            if (visualRoot == null)
            {
                PlayOpenPunch();
                return;
            }

            visualRoot.localScale = _baseScale;

            // Phase 1 - trembles in place, still closed, amplitude CRESCENDOING toward the punch.
            // PrimeTween's default ShakeScale falls off (Ease.OutQuad - starts violent, settles to
            // still), the opposite of a buildup; the rising shakeBuildupCurve (0 -> 1) inverts that
            // so the shake grows from nothing to full right as PlayOpenPunch fires. PlayOpenPunch
            // (phase 2) only starts once this completes.
            var buildup = new ShakeSettings(shakeBuildupStrength, shakeBuildupDuration, shakeBuildupFrequency,
                strengthOverTime: shakeBuildupCurve, useUnscaledTime: useUnscaledTime);
            _buildupTween = Tween.ShakeScale(visualRoot, buildup)
                .OnComplete(PlayOpenPunch);
        }

        // Phase 2 - particle + sprite swap fire the instant the punch starts, not staggered off
        // its completion.
        private void PlayOpenPunch()
        {
            // Fire-and-forget through the pooled manager so the burst outlives the chest entity
            // (destroyed ~1 frame after the screen closes) instead of being torn down mid-play.
            if (openEffectPrefab != null && EffectsManager.Instance != null)
                EffectsManager.Instance.PlayEffect(openEffectPrefab, transform.position + openEffectOffset, Quaternion.identity);

            SwapToOpenSprite();

            if (visualRoot == null)
                return;

            _punchTween.Stop();
            _punchTween = Tween.PunchScale(visualRoot, punchStrength, punchDuration, punchFrequency, useUnscaledTime: useUnscaledTime);
        }

        // Undoes TestOpenAnimation above - stops both phases if either is still running and restores
        // the closed sprite and base scale, so you can re-trigger the test from a clean state without
        // reloading the scene. The open particle isn't touched here: it's a pooled, external
        // EffectsManager one-shot (see openEffectPrefab), not a child this component owns.
        [Button]
        public void ResetView()
        {
            _buildupTween.Stop();
            _punchTween.Stop();

            if (visualRoot != null)
                visualRoot.localScale = _baseScale;

            if (spriteRenderer != null)
                spriteRenderer.sprite = _defaultSprite;
        }

        private void SwapToOpenSprite()
        {
            if (spriteRenderer != null && openSprite != null)
                spriteRenderer.sprite = openSprite;
        }

        protected override void QUpdate(QuantumGame game)
        {
        }
    }
}
