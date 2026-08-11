namespace Quantum
{
    using Photon.Deterministic;

    // Shared helpers for Pixie's Ascension lines - see docs/pixie-ascensions.md. Several lines
    // (Cluster Bomb, Pocket Bombs, Backblast) scale off "Bunny Bomb damage" rather than a fixed
    // number, per design - this resolves the same base value ProjectileSkillData.Fire itself throws
    // with (CharacterData.HeroSkill's own Damage field plus any ProjectileDamageUpgrade multiplier),
    // so all three stay in sync with whatever Bunny Bomb currently throws for without duplicating
    // that resolution three separate times.
    //
    // Returns the BASE damage value, not run through DamageUtility.ResolveOutgoingDamage - callers
    // pass DamagePercent * this into their own HitEffectUtility.ApplyExplosion/DamageUtility.
    // ApplyDamage call, which resolves the full live multiplier stack (CharacterStats.DamageMultiplier,
    // crit, Unstable Targeting, etc.) exactly once for that explosion. Resolving it here too would
    // double-apply those multipliers.
    public static unsafe class PixieAscensionUtility
    {
        public static FP ResolveBunnyBombDamage(Frame f, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(owner, out var stats) == false || stats->CharacterData.IsValid == false)
                return FP._0;

            CharacterData data = f.FindAsset(stats->CharacterData);

            if (data.HeroSkill.IsValid == false)
                return FP._0;

            if (f.FindAsset(data.HeroSkill) is not ProjectileSkillData projectileSkill)
                return FP._0;

            FP damage = projectileSkill.Damage;

            if (f.Unsafe.TryGetPointer<ProjectileDamageUpgrade>(owner, out var upgrade) == true)
                damage *= upgrade->Multiplier;

            return damage;
        }
    }
}
