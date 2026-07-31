namespace Quantum
{
    using Photon.Deterministic;

    // Passive Ascension - increases the Protector Aura's radius. Additive on top of
    // ProtectorPassiveData's own authored Radius.
    public unsafe partial class BulwarkPassiveUpgradeData : PassiveUpgradeData
    {
        public FP RadiusBonus = 3;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<ProtectorAura>(entity, out var aura) == false)
                return;

            aura->Radius += RadiusBonus;
        }
    }
}
