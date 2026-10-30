using QuantumUser.View.Util;

namespace Quantum
{
    using UnityEngine;

    // View of a BossTornado hazard (Dono do Céu). Polls the predicted frame each frame: while the simulation is in the
    // compression tell (BossTornado.Compressing) the funnel visibly contracts and spins up, telling players the pulse
    // is coming; otherwise it holds its authored size. Pulse/dissipate one-shots are BossHazardFxDirector's job.
    public class BossTornadoView : CustomQuantumEntityViewComponent
    {
        [SerializeField, Tooltip("The whirlwind visual that contracts during the compression tell.")]
        private Transform funnel;
        [SerializeField] private float compressedScale = 0.55f;
        [SerializeField] private float compressedSpinMultiplier = 2.5f;
        [SerializeField] private float scaleLerpSpeed = 10f;

        private Vector3 _baseScale = Vector3.one;
        private ParticleSystem[] _systems = System.Array.Empty<ParticleSystem>();
        private float[] _baseSimulationSpeeds = System.Array.Empty<float>();

        public override void Awake()
        {
            base.Awake();

            if (funnel == null)
                funnel = transform;

            _baseScale = funnel.localScale;
            _systems = funnel.GetComponentsInChildren<ParticleSystem>(true);
            _baseSimulationSpeeds = new float[_systems.Length];

            for (int i = 0; i < _systems.Length; i++)
                _baseSimulationSpeeds[i] = _systems[i].main.simulationSpeed;
        }

        public override void Initialize(QuantumGame game)
        {
            base.Initialize(game);
            funnel.localScale = _baseScale;
            SetSpin(1f);
        }

        protected override void QUpdate(QuantumGame game)
        {
            Frame frame = game.Frames.Predicted;

            if (frame == null || frame.TryGet<BossTornado>(_entityRef, out BossTornado tornado) == false)
                return;

            float target = tornado.Compressing ? compressedScale : 1f;
            Vector3 targetScale = _baseScale * target;
            funnel.localScale = Vector3.Lerp(funnel.localScale, targetScale, 1f - Mathf.Exp(-scaleLerpSpeed * Time.deltaTime));
            SetSpin(tornado.Compressing ? compressedSpinMultiplier : 1f);
        }

        private void SetSpin(float multiplier)
        {
            for (int i = 0; i < _systems.Length; i++)
            {
                ParticleSystem.MainModule main = _systems[i].main;
                main.simulationSpeed = _baseSimulationSpeeds[i] * multiplier;
            }
        }
    }
}
