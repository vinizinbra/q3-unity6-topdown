using QuantumUser.View.Managers;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Zara's Resonance passive - one-shot VFX for every FirePulse (see ResonanceUtility).
    // ResonancePulseReleased and ShockwaveReleased always fire together for the same pulse (see
    // FirePulse), at the same position/radius, so this plays a single particle off ShockwaveReleased
    // only rather than two overlapping instances - ResonancePulseReleased needs no subscriber here.
    // ShockwaveReleased is shared/global (it also fires for Kai's Dash Shockwave and the Empty
    // Chamber weapon perk), so the handler filters to this entity's own EntityRef.
    //
    // Tinted by remixColors when e.Effect is valid - i.e. only on a pulse where the Remix ascension
    // actually triggered (see ResonanceUtility.ResolveRemixEffect) - so the pulse visibly matches
    // whichever status it just applied (Burn/Void/Slow/Stun); defaultColor otherwise. Actual
    // playback is delegated to EffectsManager.Instance.PlayEffect so this reuses the same pooling
    // instead of duplicating it - EffectsManager's own generic OnShockwaveReleased skips playing
    // entirely whenever e.Effect is valid (see that method's own comment), so a Remix pulse never
    // plays two overlapping particles.
    public class ResonanceFxView : CustomQuantumEntityViewComponent
    {
        [SerializeField, Tooltip("Played once per Resonance pulse. Skipped if left empty.")]
        private ParticleSystem pulseEffectPrefab;

        [Header("Remix colors - keyed by which HitEffectData was randomly chosen")]
        [SerializeField] private Color burnColor = new Color(1f, 0.45f, 0.1f);
        [SerializeField] private Color voidColor = new Color(0.6f, 0.3f, 0.85f);
        [SerializeField] private Color slowColor = new Color(0.4f, 0.75f, 1f);
        [SerializeField] private Color stunColor = new Color(1f, 0.9f, 0.2f);
        [SerializeField, Tooltip("Used for a plain (non-Remix) pulse, and for any HitEffectData type not listed above.")]
        private Color defaultColor = Color.white;

        public override void Awake()
        {
            base.Awake();

            QuantumEvent.Subscribe<EventShockwaveReleased>(this, OnShockwaveReleased);
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            QuantumEvent.UnsubscribeListener(this);
        }

        protected override void QUpdate(QuantumGame game)
        {
        }

        private void OnShockwaveReleased(EventShockwaveReleased e)
        {
            if (e.Entity != _entityRef || pulseEffectPrefab == null || EffectsManager.Instance == null)
                return;

            Color color = defaultColor;

            if (e.Effect.IsValid == true)
            {
                Frame frame = e.Game.Frames.Predicted;

                if (frame != null)
                {
                    color = ResolveRemixColor(frame.FindAsset(e.Effect));
                }
            }

            EffectsManager.Instance.PlayEffect(pulseEffectPrefab, e.Position.ToUnityVector3(), Quaternion.identity, Vector3.one * e.Radius.AsFloat, color);
        }

        private Color ResolveRemixColor(HitEffectData effect)
        {
            switch (effect)
            {
                case BurnEffectData: return burnColor;
                case VoidEffectData: return voidColor;
                case SlowEffectData: return slowColor;
                case StunEffectData: return stunColor;
                default: return defaultColor;
            }
        }
    }
}
