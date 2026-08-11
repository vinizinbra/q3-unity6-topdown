namespace Quantum
{
    using UnityEngine;

    // View-only half of ConcussiveImpactSkillAction (see the partial declaration in
    // ConcussiveImpactSkillAction.cs). Carried forward from the deleted
    // JuggernautLandingImpactSkillAction.View.cs.
    public partial class ConcussiveImpactSkillAction
    {
        [Tooltip("Impact VFX authored for a radius-1 landing, scaled uniformly by the landed enemy's own real collider radius when played. Leave empty to fall back to EffectsManager's default area blast effect.")]
        public ParticleSystem ImpactEffectPrefab;
    }
}
