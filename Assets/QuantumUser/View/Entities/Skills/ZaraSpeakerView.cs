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

        public override void Awake()
        {
            base.Awake();

            CaptureRestScale(lightGreenEffect);
            CaptureRestScale(lightPurpleEffect);

            Reset(lightGreenEffect);
            Reset(lightPurpleEffect);
            SnapDetail(lightGreenEffect, active: true);

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
            Pulse(lightGreenEffect, lightPurpleEffect);
        }

        [Button("Light Purple")]
        private void PlayLightPurple()
        {
            Pulse(lightPurpleEffect, lightGreenEffect);
        }

        private void Pulse(SpeakerEffect effect, SpeakerEffect otherEffect)
        {
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
    }
}
