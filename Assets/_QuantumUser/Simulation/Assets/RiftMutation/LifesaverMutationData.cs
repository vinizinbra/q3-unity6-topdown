namespace Quantum
{
    using Photon.Deterministic;

    // Once per run: a lethal hit leaves you at 1 HP, immune, with Duration seconds to get a kill - land
    // it and you heal SurviveHeal of your Max Health, miss it and you go down anyway. See
    // MutationCombatUtility (trigger/kill/expiry) and HeroSkillStateUtility (the countdown).
    public unsafe class LifesaverMutationData : RiftMutationData
    {
        public FP Duration = 5;
        public FP SurviveHeal = FP.FromString("0.30");
        public byte Charges = 1;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->LifesaverCharges = Charges;
            stats->LifesaverDuration = Duration;
            stats->LifesaverSurviveHealFraction = SurviveHeal;
        }

        protected override object[] DescriptionArgs => new object[] { Duration.AsFloat, SurviveHeal.AsFloat * 100f };
    }
}
