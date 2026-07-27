using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Ground-plane circle showing a Sentry chassis's own Range, same world-space LineRenderer
    // approach as WeaponRangeIndicatorView (the player's equivalent) - drawn around the chassis
    // rather than a sprite, resolution is author-controlled. Unlike the player version, there's no
    // KCC.Data.IsGrounded concept to gate on (a sentry is always "grounded", never airborne), so
    // this just fades in once on spawn and stays visible for the sentry's whole lifetime instead of
    // toggling on landing/takeoff.
    [RequireComponent(typeof(LineRenderer))]
    public class SentryRangeIndicatorView : CustomQuantumEntityViewComponent
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
            bool hasSentry = frame.Has<Sentry>(_entityRef);

            UpdateFade(hasSentry);

            if (_currentAlpha <= 0f)
            {
                rangeLine.enabled = false;
                return;
            }

            rangeLine.enabled = true;

            if (hasSentry)
            {
                DrawCircle(frame.Get<Sentry>(_entityRef).Range.AsFloat);
            }
        }

        private void UpdateFade(bool hasSentry)
        {
            if (hasSentry == false)
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
