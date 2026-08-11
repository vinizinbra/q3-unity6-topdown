using System;
using System.Collections.Generic;
using NaughtyAttributes;
using PrimeTween;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    public class ZaraSpeakerView : CustomQuantumEntityViewComponent
    {
        [Serializable]
        private class SpeakerEffect
        {
            public List<SpriteRenderer> Sprites = new();
            public ParticleSystem Particle;
            public SpriteRenderer Detail;
            public Transform DetailRoot;

            [NonSerialized] public Vector3 RestScale = Vector3.one;
            [NonSerialized] public Sequence Sequence;
            [NonSerialized] public Tween IdleTween;
        }

        [SerializeField] private float fadeInDuration = 0.3f;
        [SerializeField] private float holdDuration = 0.5f;
        [SerializeField] private float fadeOutDuration = 0.3f;

        [Header("Detail Punch")]
        [SerializeField] private float detailPunchStrength = 0.3f;
        [SerializeField] private float detailPunchDuration = 0.28f;

        [Header("Detail Idle")]
        [SerializeField] private float idleScaleAmplitude = 0.05f;
        [SerializeField] private float idleDuration = 1.2f;
        [SerializeField] private Ease idleEase = Ease.InOutSine;

        [SerializeField] private SpeakerEffect lightGreenEffect;
        [SerializeField] private SpeakerEffect lightPurpleEffect;

        [Header("Bump Sprite (random, non-repeating per pulse)")]
        [SerializeField, Tooltip("Its sprite is swapped on every pulse to a random one drawn from bumpSprites.")]
        private SpriteRenderer bumpSprite;
        [SerializeField, Tooltip("Pool bumpSprite draws from. Shuffle-bag order: each sprite is shown once before any repeats, and a fresh shuffle never opens on the one the previous bag closed with (so no immediate repeat across pulses either).")]
        private List<Sprite> bumpSprites = new();

        [Header("Heal / Damage Tint")]
        [SerializeField, Tooltip("Recolored on every pulse (RGB only - each keeps its own alpha): healColor on a healing pulse, damageColor on a damage pulse.")]
        private List<SpriteRenderer> tintedSprites = new();
        [SerializeField] private Color healColor = new Color(0.4f, 1f, 0.5f);
        [SerializeField] private Color damageColor = new Color(0.7f, 0.35f, 1f);

        [Header("Secondary Group - color glow (flash + fade out)")]
        [SerializeField, Tooltip("A second group - unlike tintedSprites (which HOLDS its color until the next pulse), these FLASH the pulse color and fade back to secondaryRestColor each pulse: a one-shot color glow, same shape as HitFeedback.")]
        private List<SpriteRenderer> secondaryTintedSprites = new();
        [SerializeField] private Color secondaryHealColor = new Color(0.6f, 1f, 0.8f);
        [SerializeField] private Color secondaryDamageColor = new Color(1f, 0.5f, 0.9f);
        [SerializeField, Tooltip("Color the glow fades back to - usually clear/transparent so the sprites vanish between pulses.")]
        private Color secondaryRestColor = Color.clear;
        [SerializeField] private float secondaryGlowDuration = 0.4f;

        private readonly List<int> _bumpBag = new();
        private int _lastBumpIndex = -1;
        private Tween[] _secondaryGlowTweens;

        public override void Awake()
        {
            base.Awake();

            CaptureRestScale(lightGreenEffect);
            CaptureRestScale(lightPurpleEffect);

            Reset(lightGreenEffect);
            Reset(lightPurpleEffect);
            SnapDetail(lightGreenEffect, active: true);

            _secondaryGlowTweens = new Tween[secondaryTintedSprites.Count];

            // Start in the heal (green-active) look so the speaker isn't blank before the first pulse.
            // The primary group holds its heal tint; the secondary glow group starts at rest - it only
            // flashes on an actual pulse, so it shouldn't glow on spawn.
            SwapBumpSprite();
            TintList(tintedSprites, healColor);
            ResetSecondaryGlow();

            QuantumEvent.Subscribe<EventAlternatingAreaPulsed>(this, OnPulsed);
        }

        private static void CaptureRestScale(SpeakerEffect effect)
        {
            if (effect.DetailRoot != null)
                effect.RestScale = effect.DetailRoot.localScale;
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            QuantumEvent.UnsubscribeListener(this);
        }

        protected override void QUpdate(QuantumGame game)
        {
        }

        // Green vs purple is assumed Heal vs Damage here - flip this if the two effects are wired
        // the other way around on the prefab.
        private void OnPulsed(EventAlternatingAreaPulsed e)
        {
            if (e.Entity != _entityRef)
                return;

            if (e.IsHealing == true)
                PlayLightGreen();
            else
                PlayLightPurple();
        }

        [Button("Light Green")]
        private void PlayLightGreen()
        {
            ApplyTint(isHealing: true);
            Pulse(lightGreenEffect, lightPurpleEffect);
        }

        [Button("Light Purple")]
        private void PlayLightPurple()
        {
            ApplyTint(isHealing: false);
            Pulse(lightPurpleEffect, lightGreenEffect);
        }

        private void Pulse(SpeakerEffect effect, SpeakerEffect otherEffect)
        {
            SwapBumpSprite();

            Reset(otherEffect);

            if (effect.Sequence.isAlive)
                effect.Sequence.Stop();

            effect.Particle?.Play();

            Sequence sequence = Sequence.Create();
            AppendFade(ref sequence, effect.Sprites, 0f, 1f, fadeInDuration);

            if (effect.Detail != null)
            {
                if (effect.IdleTween.isAlive)
                    effect.IdleTween.Stop();

                effect.Detail.enabled = true;
                effect.DetailRoot.localScale = effect.RestScale;

                Tween.PunchScale(effect.DetailRoot, effect.RestScale * detailPunchStrength, detailPunchDuration)
                    .OnComplete(effect, e => StartIdle(e));
            }

            sequence = sequence.ChainDelay(holdDuration);
            AppendFade(ref sequence, effect.Sprites, 1f, 0f, fadeOutDuration);

            effect.Sequence = sequence;
        }

        // Instantly cuts an effect that isn't the one being triggered - aura sprites, particle,
        // and detail all snap off rather than finishing their own fade-out, so the two colors
        // never show on screen at the same time.
        private void Reset(SpeakerEffect effect)
        {
            if (effect.Sequence.isAlive)
                effect.Sequence.Stop();

            effect.Particle?.Stop();

            SetSpritesAlpha(effect.Sprites, 0f);

            SnapDetail(effect, active: false);
        }

        private static void SetSpritesAlpha(List<SpriteRenderer> sprites, float alpha)
        {
            foreach (SpriteRenderer sprite in sprites)
            {
                if (sprite == null)
                    continue;

                Color color = sprite.color;
                color.a = alpha;
                sprite.color = color;
            }
        }

        // Sets Detail to its settled (non-punching) state - enabled at rest scale and idling, or
        // disabled - without playing the punch. Used both for the initial Awake state and for
        // turning off the effect not being triggered.
        private void SnapDetail(SpeakerEffect effect, bool active)
        {
            if (effect.Detail == null)
                return;

            if (effect.IdleTween.isAlive)
                effect.IdleTween.Stop();

            effect.Detail.enabled = active;
            effect.DetailRoot.localScale = effect.RestScale;

            if (active)
                StartIdle(effect);
        }

        private void StartIdle(SpeakerEffect effect)
        {
            if (effect.Detail == null || effect.Detail.enabled == false)
                return;

            if (effect.IdleTween.isAlive)
                effect.IdleTween.Stop();

            effect.DetailRoot.localScale = effect.RestScale;
            effect.IdleTween = Tween.Scale(effect.DetailRoot, effect.RestScale, effect.RestScale * (1f + idleScaleAmplitude), idleDuration, idleEase, cycles: -1, cycleMode: CycleMode.Yoyo);
        }

        private static void AppendFade(ref Sequence sequence, List<SpriteRenderer> sprites, float from, float to, float duration)
        {
            bool isFirst = true;

            foreach (SpriteRenderer sprite in sprites)
            {
                if (sprite == null)
                    continue;

                Tween tween = Tween.Alpha(sprite, from, to, duration);
                sequence = isFirst ? sequence.Chain(tween) : sequence.Group(tween);
                isFirst = false;
            }
        }

        // Swaps bumpSprite to the next sprite from a shuffle bag - every entry in bumpSprites is
        // shown once before any repeats, and a freshly refilled bag never opens on the sprite the
        // previous bag closed with, so there's never an immediate repeat across pulses either.
        private void SwapBumpSprite()
        {
            if (bumpSprite == null || bumpSprites == null || bumpSprites.Count == 0)
                return;

            if (bumpSprites.Count == 1)
            {
                bumpSprite.sprite = bumpSprites[0];
                return;
            }

            if (_bumpBag.Count == 0)
                RefillBumpBag();

            int last = _bumpBag.Count - 1;
            int index = _bumpBag[last];
            _bumpBag.RemoveAt(last);
            _lastBumpIndex = index;

            bumpSprite.sprite = bumpSprites[index];
        }

        private void RefillBumpBag()
        {
            _bumpBag.Clear();

            for (int i = 0; i < bumpSprites.Count; i++)
                _bumpBag.Add(i);

            // Fisher-Yates shuffle (draws come off the END of the bag, so shuffle the whole thing).
            for (int i = _bumpBag.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (_bumpBag[i], _bumpBag[j]) = (_bumpBag[j], _bumpBag[i]);
            }

            // Don't let the next draw (bag's end) immediately repeat the last sprite shown.
            int end = _bumpBag.Count - 1;
            if (end > 0 && _bumpBag[end] == _lastBumpIndex)
                (_bumpBag[end], _bumpBag[0]) = (_bumpBag[0], _bumpBag[end]);
        }

        // Both tint groups react to this pulse: the primary group HOLDS the heal/damage color until
        // the next pulse; the secondary group flashes it and fades out (a one-shot color glow).
        private void ApplyTint(bool isHealing)
        {
            TintList(tintedSprites, isHealing ? healColor : damageColor);
            GlowSecondary(isHealing);
        }

        // Secondary group's "color glow" - snaps to the pulse color, then tweens back to
        // secondaryRestColor, the same flash-then-fade shape as HitFeedback.ApplyFlash. The per-sprite
        // tween is stored so a rapid second pulse restarts it cleanly instead of stacking two color
        // tweens on the same renderer.
        private void GlowSecondary(bool isHealing)
        {
            if (secondaryTintedSprites == null || _secondaryGlowTweens == null)
                return;

            Color color = isHealing ? secondaryHealColor : secondaryDamageColor;

            for (int i = 0; i < secondaryTintedSprites.Count && i < _secondaryGlowTweens.Length; i++)
            {
                SpriteRenderer sprite = secondaryTintedSprites[i];
                if (sprite == null)
                    continue;

                _secondaryGlowTweens[i].Stop();
                sprite.color = color;
                _secondaryGlowTweens[i] = Tween.Color(sprite, color, secondaryRestColor, secondaryGlowDuration);
            }
        }

        // Snaps the secondary group to its rest color with no glow (Awake / initial state).
        private void ResetSecondaryGlow()
        {
            if (secondaryTintedSprites == null || _secondaryGlowTweens == null)
                return;

            for (int i = 0; i < secondaryTintedSprites.Count && i < _secondaryGlowTweens.Length; i++)
            {
                SpriteRenderer sprite = secondaryTintedSprites[i];
                if (sprite == null)
                    continue;

                _secondaryGlowTweens[i].Stop();
                sprite.color = secondaryRestColor;
            }
        }

        // Recolors every entry's RGB while keeping each one's own alpha.
        private static void TintList(List<SpriteRenderer> sprites, Color color)
        {
            if (sprites == null)
                return;

            foreach (SpriteRenderer sprite in sprites)
            {
                if (sprite == null)
                    continue;

                color.a = sprite.color.a;
                sprite.color = color;
            }
        }
    }
}
