using Photon.Deterministic;
using QuantumUser.View.Managers;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Ground telegraph for Afterbeat's own delay window (rank 2+: ~1s between the dash and the
    // delayed pulse actually landing, at both the Start and End position independently at rank 3 -
    // see ZaraAfterbeat.qtn/ZaraAfterbeatSystem). Polls ZaraAfterbeat.StartRemaining/EndRemaining
    // directly every QUpdate rather than reacting to a fire-once event - same "poll a live countdown
    // component field" idiom MaxImmortalView/PixieBombView/EnemyAttackVisualsView already use for
    // this exact shape of problem (a duration that's already ticking down deterministically in the
    // simulation needs no separate event to stay in sync across rollback). The pulse's own landing
    // VFX (reacting to AfterbeatPulseReleased) still fires separately at the instant
    // Remaining hits 0 - this view only covers the WAIT, not the impact.
    //
    // Reuses the existing enemy-telegraph pooling (TelegraphManager/TelegraphFade/TelegraphGrow,
    // CircleTelegraph.prefab) rather than inventing a new ring visual - a ground warning circle that
    // grows-and-fades over a known duration is exactly the same shape whether an enemy or Zara's own
    // Afterbeat is the one telegraphing.
    public class ZaraAfterbeatTelegraphView : CustomQuantumEntityViewComponent
    {
        [SerializeField, Tooltip("Ground ring shown for the delay window. Falls back to TelegraphManager's own Circle shape default if left empty. Skipped entirely if neither resolves.")]
        private GameObject telegraphPrefab;
        [SerializeField] private float fadeInDuration = 0.15f;
        [SerializeField] private float fadeOutDuration = 0.15f;

        private GameObject _startInstance;
        private GameObject _endInstance;
        private bool _startActive;
        private bool _endActive;

        public override void DeInitialize(QuantumGame game)
        {
            base.DeInitialize(game);

            ReleaseImmediate(ref _startInstance);
            ReleaseImmediate(ref _endInstance);
            _startActive = false;
            _endActive = false;
        }

        protected override void QUpdate(QuantumGame game)
        {
            Frame frame = game.Frames.Predicted;

            if (frame.TryGet<ZaraAfterbeat>(_entityRef, out var afterbeat) == false)
            {
                UpdateSlot(ref _startInstance, ref _startActive, FP._0, default, FP._0);
                UpdateSlot(ref _endInstance, ref _endActive, FP._0, default, FP._0);
                return;
            }

            UpdateSlot(ref _startInstance, ref _startActive, afterbeat.StartRemaining, afterbeat.StartPosition, afterbeat.StartRadius);
            UpdateSlot(ref _endInstance, ref _endActive, afterbeat.EndRemaining, afterbeat.EndPosition, afterbeat.EndRadius);
        }

        private void UpdateSlot(ref GameObject instance, ref bool active, FP remaining, FPVector3 position, FP radius)
        {
            bool shouldBeActive = remaining > FP._0;

            if (shouldBeActive == active)
                return;

            active = shouldBeActive;

            if (shouldBeActive == true)
            {
                Spawn(ref instance, position, radius.AsFloat, remaining.AsFloat);
            }
            else if (instance != null)
            {
                ReleaseFadeOut(instance);
                instance = null;
            }
        }

        private void Spawn(ref GameObject instance, FPVector3 position, float radius, float duration)
        {
            GameObject prefab = ResolvePrefab();

            if (prefab == null || TelegraphManager.Instance == null)
                return;

            // Same pose convention as EnemyAttackVisualsView.ComputeTelegraphPose's Circle branch -
            // lie flat on the ground (not camera-facing, the sprite's default) and scale to the
            // pulse's own real hit radius, so this shares a pooled prefab with enemy telegraphs
            // without inheriting whatever pose an enemy attack last left on the recycled instance.
            Quaternion rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
            instance = TelegraphManager.Instance.Get(prefab, position.ToUnityVector3(), rotation);

            if (instance != null)
                instance.transform.localScale = new Vector3(radius * 2f, radius * 2f, 1f);

            if (instance != null && instance.TryGetComponent(out TelegraphFade fade) == true)
                fade.Initialize(prefab, fadeInDuration, fadeOutDuration, duration, _entityRef);
        }

        private void ReleaseFadeOut(GameObject instance)
        {
            if (instance.TryGetComponent(out TelegraphFade fade) == true)
                fade.FadeOutAndRelease();
        }

        private void ReleaseImmediate(ref GameObject instance)
        {
            if (instance == null)
                return;

            GameObject prefab = ResolvePrefab();

            if (TelegraphManager.Instance != null && prefab != null)
                TelegraphManager.Instance.Release(prefab, instance);

            instance = null;
        }

        private GameObject ResolvePrefab()
        {
            if (telegraphPrefab != null)
                return telegraphPrefab;

            return TelegraphManager.Instance != null
                && TelegraphManager.Instance.TryGetDefaultPrefab(TelegraphShape.Circle, out GameObject fallback) == true
                ? fallback
                : null;
        }
    }
}
