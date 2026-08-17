namespace Quantum
{
    using Photon.Deterministic;

    // Shared helpers for Zara's Ascension lines - see docs/zara-ascensions.md.
    public static unsafe class ZaraAscensionUtility
    {
        // "Hero Skill Damage" - the percentage basis Afterbeat's own delayed-pulse damage scales off
        // (see AfterbeatSkillAction, spec's own "damage scales from Hero Skill Damage, not fixed 20").
        // Reactivates ZaraBaseSkill.Damage, previously a dead field (the Totem's real Damage Beat
        // amount lives on SpawnAlternatingAreaEffectData.DamageAmount instead). Mirrors
        // KaiAscensionUtility.ResolveVortexSkillDamage/BruteAscensionUtility.
        // ResolveJuggernautSkillDamage exactly - resolved via CharacterStats.CharacterData ->
        // CharacterData.HeroSkill rather than a direct asset reference, so it always reflects
        // whichever hero/skill asset the entity actually has equipped.
        //
        // Returns the BASE damage value, not run through DamageUtility.ResolveOutgoingDamage - every
        // caller feeds its own computed number into DamageUtility.ApplyDamage, which resolves the
        // full live multiplier stack exactly once. Resolving it here too would double-apply it.
        public static FP ResolveHeroSkillDamage(Frame f, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(owner, out var stats) == false || stats->CharacterData.IsValid == false)
                return FP._0;

            CharacterData data = f.FindAsset(stats->CharacterData);

            if (data.HeroSkill.IsValid == false)
                return FP._0;

            if (f.FindAsset(data.HeroSkill) is not ProjectileSkillData totemSkill)
                return FP._0;

            return totemSkill.Damage;
        }
    }
}
