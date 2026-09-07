namespace Quantum
{
    using UnityEngine;

    // View-only half of JuggernautSkillData (see the partial declaration in JuggernautSkillData.cs).
    public partial class JuggernautSkillData
    {
        [Tooltip("Discharge pulse VFX authored for a radius-1 knockback, scaled uniformly by Radius when played. Leave empty to fall back to EffectsManager's default area blast effect.")]
        public ParticleSystem DischargeEffectPrefab;

        [Tooltip("Hit-impact VFX played on each individual enemy struck by a discharge, at that enemy's own position. Leave empty to fall back to DischargeEffectPrefab, then EffectsManager's default area blast effect.")]
        public ParticleSystem DischargeHitEffectPrefab;
    }
}
