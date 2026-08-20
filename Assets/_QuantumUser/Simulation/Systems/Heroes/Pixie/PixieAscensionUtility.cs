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
        // A PixieBombCharge field left at 0 means "the line that owns it isn't picked" - read as a
        // neutral 1 rather than annihilating the product. Every consumption site goes through this
        // instead of repeating the same guard, and it's what lets each dash line own its own fields
        // (see PixieBombCharge.qtn) without either having to know the other exists.
        public static FP Neutral(FP multiplier) => multiplier > FP._0 ? multiplier : FP._1;

        // Refreshes the shared "next Bunny Bomb is empowered" window without clobbering whichever
        // line set it first this same dash - both dash lines' Execute run in the same Begin phase, so
        // the longer of the two windows wins.
        public static void ExtendBombChargeWindow(PixieBombCharge* charge, FP window)
        {
            charge->Remaining = FPMath.Max(charge->Remaining, window);
        }

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
