using NaughtyAttributes;
using PrimeTween;
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
    // slow to a crawl right along with it. The entity itself lingers for
    // ChestSystem.OpenLingerDuration (via the generic DestroyAfterTime) specifically so this
    // sequence has time to finish before the GameObject is destroyed.
    //
    // Targets visualRoot, NOT this component's own transform - QuantumEntityView drives that
    // transform every frame from the simulation's own Transform3D and would fight a tween applied
    // directly to it (same reasoning PixieBombView's own visualRoot comment gives).
    public class ChestView : CustomQuantumEntityViewComponent
    {
        [SerializeField] private Transform visualRoot;
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private Sprite openSprite;
        [SerializeField, Tooltip("Played once at the start of the punch-open phase, together with the sprite swap - leave unassigned to skip.")]
        private ParticleSystem openParticle;

        [Header("Phase 1 - shake-scale buildup")]
        [SerializeField] private Vector3 shakeBuildupStrength = new Vector3(0.08f, 0.08f, 0f);
        [SerializeField] private float shakeBuildupDuration = 0.5f;
        [SerializeField] private float shakeBuildupFrequency = 18f;

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

            // Phase 1 - trembles in place, still closed. PlayOpenPunch (phase 2) only starts once
            // this settles.
            _buildupTween = Tween.ShakeScale(visualRoot, shakeBuildupStrength, shakeBuildupDuration, shakeBuildupFrequency, useUnscaledTime: useUnscaledTime)
                .OnComplete(PlayOpenPunch);
        }

        // Phase 2 - particle + sprite swap fire the instant the punch starts, not staggered off
        // its completion.
        private void PlayOpenPunch()
        {
            if (openParticle != null)
                openParticle.Play();

            SwapToOpenSprite();

            if (visualRoot == null)
                return;

            _punchTween.Stop();
            _punchTween = Tween.PunchScale(visualRoot, punchStrength, punchDuration, punchFrequency, useUnscaledTime: useUnscaledTime);
        }

        // Undoes TestOpenAnimation above - stops both phases if either is still running, restores
        // the closed sprite and base scale, and clears any particles still playing, so you can
        // re-trigger the test from a clean state without reloading the scene.
        [Button]
        public void ResetView()
        {
            _buildupTween.Stop();
            _punchTween.Stop();

            if (visualRoot != null)
                visualRoot.localScale = _baseScale;

            if (spriteRenderer != null)
                spriteRenderer.sprite = _defaultSprite;

            if (openParticle != null)
                openParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
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
