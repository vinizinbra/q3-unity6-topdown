namespace Quantum
{
    using UnityEngine;

    // View-only half of EruptionLineDeliveryData, read by EruptionRumbleFxDirector: a looping "the ground is about to
    // burst" effect played on every eruption point for exactly its warning countdown (scaled by the eruption radius).
    public partial class EruptionLineDeliveryData
    {
        public ParticleSystem WarningEffect;
    }
}
