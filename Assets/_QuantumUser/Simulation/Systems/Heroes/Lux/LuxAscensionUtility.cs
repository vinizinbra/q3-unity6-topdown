namespace Quantum
{
    using Photon.Deterministic;

    // Shared helpers for Lux's Ascension lines - mirrors BruteAscensionUtility/KaiAscensionUtility/
    // PixieAscensionUtility exactly.
    public static unsafe class LuxAscensionUtility
    {
        // "Sentry Skill Damage" - the percentage basis every Lux Ascension that deals damage in its own
        // right scales off (Overload Core today, and any future one). Resolved via
        // CharacterStats.CharacterData -> CharacterData.HeroSkill rather than a direct asset reference,
        // so it always reflects whichever hero/skill asset the entity actually has equipped.
        //
        // Returns the BASE damage value, not run through DamageUtility.ResolveOutgoingDamage - every
        // caller feeds its own computed number into DamageUtility.ApplyDamage/ApplyDamageInRadius,
        // which resolves the full live multiplier stack exactly once. Resolving it here too would
        // double-apply those multipliers.
        public static FP ResolveSentrySkillDamage(Frame f, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(owner, out var stats) == false || stats->CharacterData.IsValid == false)
                return FP._0;

            CharacterData data = f.FindAsset(stats->CharacterData);

            if (data.HeroSkill.IsValid == false)
                return FP._0;

            SkillData heroSkill = f.FindAsset(data.HeroSkill);

            // The Sentry skill is an InstantSkillData whose Actions include SpawnSentrySkillAction -
            // the damage basis lives on that action rather than on the skill asset itself (unlike
            // Brute/Kai, whose Hero Skills are bespoke SkillData subclasses with their own Damage
            // field). Scanning the skill's own Actions list keeps this working regardless of which
            // slot/order it's authored in.
            if (heroSkill == null)
                return FP._0;

            for (int i = 0; i < heroSkill.Actions.Count; i++)
            {
                if (heroSkill.Actions[i].IsValid == false)
                    continue;

                if (f.FindAsset(heroSkill.Actions[i]) is SpawnSentrySkillAction spawn)
                    return spawn.SkillDamage;
            }

            return FP._0;
        }
    }
}
