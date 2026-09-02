using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Ground-plane circle showing the equipped weapon's Range, drawn as world-space LineRenderer
    // points around the character rather than a sprite - resolution is author-controlled.
    // Fades in on landing over fadeDuration, but disappears instantly the moment KCC.Data.IsGrounded
    // goes false - not worth also fading out, since by the time it'd finish the character is
    // already airborne and the range is no longer usable info.
    [RequireComponent(typeof(LineRenderer))]
    public class WeaponRangeIndicatorView : CustomQuantumEntityViewComponent
    {
        [SerializeField, Tooltip("Number of points around the circle - higher = smoother.")]
        private int resolution = 64;
        [SerializeField] private float groundOffset = 0.02f;
        [SerializeField] private float fadeDuration = 0.2f;
        [SerializeField] private Color rangeColor = new Color(1f, 1f, 1f, 0.5f);

        private LineRenderer rangeLine;
        private float _currentAlpha;

        public override void Awake()
        {
            base.Awake();

            // Only makes sense on the local player's own screen - a remote teammate's (or bot's)
            // weapon range must never leak onto this client, same idiom as MovementRingView.
            executeOnlyOnLocal = true;

            rangeLine = GetComponent<LineRenderer>();
        }

        public override void Initialize(QuantumGame game)
        {
            base.Initialize(game);

            rangeLine.loop = true;
            rangeLine.useWorldSpace = true;
            rangeLine.positionCount = resolution;

            _currentAlpha = 0f;
            rangeLine.enabled = false;
        }

        protected override void QUpdate(QuantumGame game)
        {
            Frame frame = game.Frames.Predicted;
            bool hasWeaponAndKcc = frame.Has<KCC>(_entityRef) && frame.Has<Weapon>(_entityRef);
            bool isGrounded = hasWeaponAndKcc && frame.Get<KCC>(_entityRef).Data.IsGrounded;

            UpdateFade(isGrounded);

            if (_currentAlpha <= 0f)
            {
                rangeLine.enabled = false;
                return;
            }

            rangeLine.enabled = true;

            if (hasWeaponAndKcc)
            {
                Weapon weapon = frame.Get<Weapon>(_entityRef);
                WeaponDataAsset weaponData = frame.FindAsset(weapon.WeaponData);
                // Mirror the sim's own effective-range formula (WeaponSystem: Range * RangeMultiplier)
                // so the circle reflects the Weapon Range global upgrade and the Long Barrel perk -
                // reading weaponData.Range alone always drew the un-upgraded base range, which is why
                // the indicator never moved when range was stacked.
                DrawCircle((weaponData.Range * weapon.RangeMultiplier).AsFloat);
            }
        }

        private void UpdateFade(bool isGrounded)
        {
            if (isGrounded == false)
            {
                _currentAlpha = 0f;
            }
            else
            {
                float fadeSpeed = fadeDuration > 0f ? 1f / fadeDuration : float.MaxValue;
                _currentAlpha = Mathf.MoveTowards(_currentAlpha, 1f, fadeSpeed * Time.deltaTime);
            }

            Color color = rangeColor;
            color.a = rangeColor.a * _currentAlpha;
            rangeLine.startColor = color;
            rangeLine.endColor = color;
        }

        private void DrawCircle(float radius)
        {
            if (rangeLine.positionCount != resolution)
                rangeLine.positionCount = resolution;

            Vector3 center = transform.position + Vector3.up * groundOffset;

            for (int i = 0; i < resolution; i++)
            {
                float angle = i / (float)resolution * Mathf.PI * 2f;
                Vector3 point = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                rangeLine.SetPosition(i, point);
            }
        }
    }
}
