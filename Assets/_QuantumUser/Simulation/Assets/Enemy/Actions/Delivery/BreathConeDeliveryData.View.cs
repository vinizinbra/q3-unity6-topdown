namespace Quantum
{
    using UnityEngine;

    // View-only half of BreathConeDeliveryData, read by BreathConeFxDirector while a breath is channelling: the looping
    // breath effect (pointed along the live Aim.Angle, so it turns with the real hit area) and the ground cone drawn
    // under it at the real range/angle.
    public partial class BreathConeDeliveryData
    {
        public ParticleSystem BreathEffect;
        [Tooltip("Fallback mouth position (no rendered rig found) relative to the breather, in its aim frame: x = side, y = up from the body pivot, z = forward.")]
        public Vector3 MouthOffset = new Vector3(0f, 0.6f, 1.2f);
        [Tooltip("The mouth on the breath pose's sprite, in sprite-local units from its pivot (art facing right). The effect starts there on the RENDERED rig (its flip, billboard tilt and fit scale included), so the breath always leaves the drawn mouth.")]
        public Vector2 MouthOnSprite = new Vector2(2.35f, 1.41f);
        public bool MouthFromSprite = true;
        [Tooltip("Uniform scale of the breath effect.")]
        public float EffectScale = 1f;
        public GameObject AreaDecal;
    }
}
