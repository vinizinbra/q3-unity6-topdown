using UnityEngine;

namespace QuantumUser.View.Util
{
    // Shared semi-implicit-Euler damped-spring integrator for BlobAnimationView's landing spring,
    // PlayerGunAimView's speed sway, and JiggleBone2D. A single Time.deltaTime step of this
    // integration is only stable while omega*dt stays small - a large dt (fps drop, loading
    // hitch, alt-tab) pushes it past that bound and the position/velocity grow exponentially
    // every step instead of settling, eventually overflowing to Infinity/NaN. That NaN then
    // reaches the transform: Mathf.Clamp does NOT filter NaN (every comparison against NaN is
    // false, so it just passes the value through), so downstream clamps on scale/position don't
    // save it.
    //
    // Substeps at a bounded max dt so each individual step stays inside the stable regime, then
    // - belt and suspenders - snaps straight back to the target if the result is non-finite
    // anyway (e.g. a multi-second debugger-pause dt that overruns even the substep cap), and
    // optionally clamps the settled distance from target so a borderline-unstable run can't read
    // as the weapon/bone flying off the character before it recovers.
    public static class DampedSpring
    {
        private const float MaxSubstepDt = 1f / 120f;
        private const int MaxSubsteps = 12;

        public static void Integrate(ref float value, ref float velocity, float target, float frequency, float damping, float dt, float maxDistanceFromTarget = 0f)
        {
            if (dt <= 0f)
                return;

            int steps = Mathf.Clamp(Mathf.CeilToInt(dt / MaxSubstepDt), 1, MaxSubsteps);
            float stepDt = dt / steps;
            float omega = frequency * Mathf.PI * 2f;

            for (int i = 0; i < steps; i++)
            {
                float force = -omega * omega * (value - target) - 2f * damping * omega * velocity;
                velocity += force * stepDt;
                value += velocity * stepDt;
            }

            if (float.IsNaN(value) || float.IsInfinity(value) || float.IsNaN(velocity) || float.IsInfinity(velocity))
            {
                value = target;
                velocity = 0f;
            }
            else if (maxDistanceFromTarget > 0f)
            {
                value = Mathf.Clamp(value, target - maxDistanceFromTarget, target + maxDistanceFromTarget);
            }
        }

        public static void Integrate(ref Vector2 value, ref Vector2 velocity, Vector2 target, Vector2 frequency, float damping, float dt, float maxDistanceFromTarget = 0f)
        {
            if (dt <= 0f)
                return;

            int steps = Mathf.Clamp(Mathf.CeilToInt(dt / MaxSubstepDt), 1, MaxSubsteps);
            float stepDt = dt / steps;
            Vector2 omega = frequency * Mathf.PI * 2f;

            for (int i = 0; i < steps; i++)
            {
                Vector2 displacement = value - target;
                Vector2 force = new Vector2(
                    -omega.x * omega.x * displacement.x - 2f * damping * omega.x * velocity.x,
                    -omega.y * omega.y * displacement.y - 2f * damping * omega.y * velocity.y);
                velocity += force * stepDt;
                value += velocity * stepDt;
            }

            if (IsNaNOrInfinity(value) || IsNaNOrInfinity(velocity))
            {
                value = target;
                velocity = Vector2.zero;
            }
            else if (maxDistanceFromTarget > 0f)
            {
                value = target + Vector2.ClampMagnitude(value - target, maxDistanceFromTarget);
            }
        }

        private static bool IsNaNOrInfinity(Vector2 v) =>
            float.IsNaN(v.x) || float.IsInfinity(v.x) || float.IsNaN(v.y) || float.IsInfinity(v.y);
    }
}
