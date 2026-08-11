namespace Quantum
{
    using Photon.Deterministic;

    // Shared helpers for Kai's Ascension lines - see docs/kai-ascensions.md.
    public static unsafe class KaiAscensionUtility
    {
        // "Vortex Skill Damage" - the percentage basis Compression/Vortex Collapse/Void Shards all
        // scale off (see KaiVortexSkill.Damage's own comment). Mirrors
        // BruteAscensionUtility.ResolveJuggernautSkillDamage exactly - resolved via CharacterStats.
        // CharacterData -> CharacterData.HeroSkill rather than a direct asset reference, so it always
        // reflects whichever hero/skill asset the entity actually has equipped.
        //
        // Returns the BASE damage value, not run through DamageUtility.ResolveOutgoingDamage - every
        // caller feeds its own computed number into DamageUtility.ApplyDamage/ApplyDamageInRadius,
        // which resolves the full live multiplier stack (CharacterStats.DamageMultiplier, crit, etc.)
        // exactly once. Resolving it here too would double-apply those multipliers.
        public static FP ResolveVortexSkillDamage(Frame f, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(owner, out var stats) == false || stats->CharacterData.IsValid == false)
                return FP._0;

            CharacterData data = f.FindAsset(stats->CharacterData);

            if (data.HeroSkill.IsValid == false)
                return FP._0;

            if (f.FindAsset(data.HeroSkill) is not ProjectileSkillData vortexSkill)
                return FP._0;

            return vortexSkill.Damage;
        }
    }
}
