namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Ranked Hero Skill Ascension - rewards accurate Bunny Bomb placement: enemies inside the inner
    // InnerRadiusFraction of any of Pixie's explosions take bonus damage (see
    // DemolitionMasteryUtility.ApplyProximityEffects, read live per blast, never baked into
    // CharacterStats).
    //
    //  - Rank 1: inner 35% of the blast deals +30%.
    //  - Rank 2: the inner zone itself widens to 45% AND hits harder (+50%) - a real expansion of the
    //    mechanic rather than a bigger number on the same tiny zone.
    //  - Rank 3: +75%, and inner-zone hits also stagger - strong knockback with an arcade falloff out
    //    to the blast edge (folded in from the old standalone Concussive Force ascension, see
    //    DirectHitUpgrade.qtn).
    //
    // MOVED from PassiveUpgradeData into the Hero Skill pool per the balance brief: it reads as part
    // of "how Bunny Bomb behaves", not as a generic passive, and the level-up UI/debug menu label it
    // by the pool it's drafted from. Behaviourally identical either way - JuggernautSkillData/
    // DemolitionMasteryUtility read DirectHitUpgrade through a plain optional TryGetPointer and never
    // cared which grant mechanism put it there (the same conversion Brute's 4 Juggernaut lines already
    // went through, see docs/brute-ascensions.md's "Hero Skill, not Passive Upgrade" section).
    //
    // Begin-only, deliberately not paired with End: this configures how her explosions behave, not a
    // buff scoped to the throw itself - revoking on End would race against the bomb's own later
    // detonation actually reading it. Re-granting fresh (idempotent) every activation sidesteps that
    // entirely, and reads the live rank fresh via selfRef so a rank-up applies to the very next throw.
    public unsafe partial class DirectHitSkillAction : SkillActionData
    {
        public FP[] InnerRadiusFraction = { FP.FromString("0.35"), FP.FromString("0.45"), FP.FromString("0.45") };
        public FP[] DamageMultiplierBonus = { FP.FromString("0.30"), FP.FromString("0.50"), FP.FromString("0.75") };

        [Header("Rank 3 - stagger")]
        public FP KnockbackForce = 8;
        public FP KnockbackUpwardForce = 2;

        [Tooltip("Scales the knockback down against Elite-tier targets. Bosses need nothing here - EnemyTierStatsConfig/BossRuntimeState already resist displacement regardless of what caused it.")]
        public FP KnockbackEliteMultiplier = FP.FromString("0.4");

        public DirectHitSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase firedPhase, AssetRef<SkillActionData> selfRef)
        {
            int rank = System.Math.Max(1, SkillUpgradeUtility.GetRank(f, filter.Entity, selfRef));
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            f.AddOrGet<DirectHitUpgrade>(filter.Entity, out var upgrade);
            upgrade->InnerRadiusFraction = InnerRadiusFraction[index];
            upgrade->DamageMultiplierBonus = DamageMultiplierBonus[index];
            upgrade->HasKnockback = rank >= 3;
            upgrade->KnockbackForce = KnockbackForce;
            upgrade->KnockbackUpwardForce = KnockbackUpwardForce;
            upgrade->KnockbackEliteMultiplier = KnockbackEliteMultiplier;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }
    }
}
