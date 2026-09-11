namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Lux's Element Mastery - Neutral (see docs/hero-mastery.md). Same weaker curve every Neutral
    // Mastery line authors (10/20/40, not the standard 15/30/50) - this instance's own authored
    // DamageMultiplierPerRank, not a special case in code.
    //  R1/R2/R3: +10% / +20% / +40% Neutral Weapon Damage.
    //  R3 "Neutral Focus": a Neutral weapon hit makes that enemy Lux's Focus Target - all of HER
    //  Sentries prioritize it while it stays in their own normal range and stays valid. Configuration
    //  only - the actual behavior is the generic, hero-agnostic Priority Target system
    //  (PriorityTargetUpgrade/PriorityTarget/PriorityTargetSystem, see PriorityTarget.qtn) - this class
    //  just installs it with Lux's own tuned values. Deliberately damage-neutral: no Sentry Damage/
    //  Fire Rate/Exposed/vulnerability of any kind - target priority only.
    public unsafe partial class LuxNeutralMasteryData : ElementMasteryData
    {
        [Tooltip("Neutral Focus (R3) - how long a Focus Target stays prioritized after the most recent qualifying hit.")]
        public FP FocusDuration = FP._2;

        protected override void ApplyRank(Frame f, EntityRef entity, int rank)
        {
            if (rank < 3)
                return;

            f.AddOrGet<PriorityTargetUpgrade>(entity, out var focus);
            focus->RequiredElement = ElementType.Neutral;
            focus->Duration = FocusDuration;
        }
    }
}
