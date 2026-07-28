namespace Quantum
{
    // A hero's innate modifier, baked into their CharacterStats once at spawn - same shape as
    // WeaponPerkData, since a passive is never lost either.
    public abstract unsafe class PassiveData : AssetObject
    {
        public abstract void Apply(Frame f, EntityRef entity, CharacterStats* stats);
    }
}
