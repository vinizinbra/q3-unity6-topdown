namespace Quantum
{
    // Doesn't touch a stat - flips CharacterStats.AllOrNothingActive, read by
    // LevelUpUtility.RollOptionsFor to force this entity's next (and every subsequent) level-up
    // roll down to a single, rarity-shifted option instead of the normal up-to-3. Plain-text
    // Description, no live values to template in - see docs/rift-mutations.md.
    public unsafe class AllOrNothingMutationData : RiftMutationData
    {
        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->AllOrNothingActive = true;
        }
    }
}
