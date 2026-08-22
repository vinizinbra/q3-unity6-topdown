namespace Quantum
{
    using UnityEngine;

    // A hero's innate modifier, baked into their CharacterStats once at spawn - same shape as
    // WeaponPerkData, since a passive is never lost either. Its display half (Icon/DisplayName)
    // lives in PassiveData.View.cs, mirroring SkillData/SkillData.View.cs.
    public abstract unsafe partial class PassiveData : AssetObject
    {
        [TextArea(2, 4)]
        public string Description;

        public abstract void Apply(Frame f, EntityRef entity, CharacterStats* stats);
    }
}
