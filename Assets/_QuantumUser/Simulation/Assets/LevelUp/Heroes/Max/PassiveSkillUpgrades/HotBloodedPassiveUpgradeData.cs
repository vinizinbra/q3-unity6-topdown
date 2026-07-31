namespace Quantum
{
    // Passive Ascension - Adrenaline builds faster (more Stacks gained per damage-dealt-or-taken
    // event). Additive on top of AdrenalineRushPassiveData's own authored GainPerHit.
    public unsafe partial class HotBloodedPassiveUpgradeData : PassiveUpgradeData
    {
        public byte GainPerHitBonus = 1;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<Adrenaline>(entity, out var adrenaline) == false)
                return;

            adrenaline->GainPerHit += GainPerHitBonus;
        }
    }
}
