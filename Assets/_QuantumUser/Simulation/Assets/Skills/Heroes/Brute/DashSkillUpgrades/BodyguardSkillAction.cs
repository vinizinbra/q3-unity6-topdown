namespace Quantum
{
    using System;
    using Photon.Deterministic;
    using UnityEngine;

    // Ranked Dash Ascension (Bodyguard) - on dash complete, Brute and every ally around him get a
    // Free Hit Guard: the next damaging hit they take is negated outright (see StatusEffects.qtn /
    // StatusEffectUtility.ApplyFreeHitGuard).
    //
    //  - Rank 1: guard lasts 2.5s. Brute is included at full value, same as any ally.
    //  - Rank 2: guard lasts 3.5s, and when one blocks, Brute gains 10 Shield.
    //  - Rank 3: when one blocks it also releases a knockback shockwave around whoever it saved, and
    //            Brute gains 15 Shield instead.
    //
    // Replaces the old "restore flat Shield to allies at dash end". That version could only ever top
    // up a bar that refilled itself anyway; now that player Shield is charge-only (see Shield.qtn) the
    // interesting protection to hand out is a guaranteed negation, and Brute's own Shield reward is
    // EARNED when a guard he placed actually saves someone - which, since Shield is what keeps his
    // Accessory on his head, means protecting the team is also how he protects his own gear.
    //
    // Because Brute guards himself too, ranks 2-3 close a real loop: guard yourself, eat a hit with
    // it, get Shield back. The per-ally cooldown below is what paces that - it applies to Brute
    // exactly as it does to everyone else, so a dash-cooldown build can't hold a permanent guard.
    //
    // THE LAYER MASK IS LOAD-BEARING, NOT DEFENSIVE. This fires at dash End, and DashSkillData parks
    // the dasher on IgnoreProjectile for the dash's whole duration to give Dash its i-frames.
    // DashSkillData.End restores the layer one line before End-phase actions run, but that is far too
    // late: Core.PhysicsSystem3D runs before every user system, so the broadphase this query reads was
    // already built this tick with Brute still on IgnoreProjectile. A plain Player-mask
    // FindPlayersInRadius therefore drops him 100% of the time, not intermittently - which is exactly
    // how the old self-restore silently never ran (see docs/brute-ascensions.md, "Bodyguard never
    // shielded Brute himself"). FindPlayersInRadiusIncludingDashing is what makes "it also triggers on
    // Brute" actually true.
    public unsafe partial class BodyguardSkillAction : SkillActionData
    {
        [Tooltip("Radius around the dash's end point that receives a guard, per rank. Brute is included. Grows every rank rather than plateauing at rank 2 - at rank 1 it is tight enough that guarding a teammate is a deliberate act of aiming the dash at them, not something that happens incidentally.")]
        public FP[] Radius = { FP._3, FP._6, FP._8 };

        [Tooltip("How long the granted Free Hit Guard lasts before it lapses unused, per rank.")]
        public FP[] GuardDuration = { FP.FromString("2.5"), FP.FromString("3.5"), FP.FromString("3.5") };

        [Tooltip("Rank 2+ - flat Shield paid back to Brute when a guard he granted actually blocks a hit, including one he placed on himself. 0 at rank 1.")]
        public FP[] ShieldReward = { FP._0, 10, 15 };

        [Tooltip("Per-RECIPIENT cooldown before another guard can be placed on that same person, Brute included. Deliberately on the recipient, not on Brute: per-Brute would still let two Brutes chain-guard one teammate, and it would punish dashing BETWEEN different allies - exactly the play this line should reward.")]
        public FP CooldownPerAlly = FP.FromString("4.5");

        [Header("Rank 3")]
        [Tooltip("Radius of the knockback shockwave released around whoever the guard just saved.")]
        public FP ShockwaveRadius = 3;

        [Tooltip("Horizontal push of that shockwave. No damage and no stun - the point is to buy space right after a near-death, not to deal damage.")]
        public FP ShockwaveForce = 4;

        public BodyguardSkillAction()
        {
            Phase = SkillActionPhase.End;
        }

        public override FP EffectRadius => Radius[Radius.Length - 1];

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase firedPhase, AssetRef<SkillActionData> selfRef)
        {
            int rank = Math.Max(1, SkillUpgradeUtility.GetRank(f, filter.Entity, selfRef));
            int index = Math.Clamp(rank, 1, (int)MaxRank) - 1;

            // Refreshed on every dash rather than written once at pick time, so the component always
            // describes the CURRENTLY picked rank - BruteBodyguardReactionSystem reads it long after
            // the dash is over, when only entity refs are left to work from.
            f.AddOrGet<BodyguardUpgrade>(filter.Entity, out var upgrade);

            upgrade->GuardDuration = GuardDuration[index];
            upgrade->ShieldReward = ShieldReward[index];
            upgrade->ShockwaveRadius = rank >= 3 ? ShockwaveRadius : FP._0;
            upgrade->ShockwaveForce = rank >= 3 ? ShockwaveForce : FP._0;

            FP guardDuration = GuardDuration[index];

            if (guardDuration <= FP._0)
                return;

            // Skill Area (CharacterStats.AreaRadiusMultiplier) applies here the same way it does to
            // every other radius in Brute's kit - see StatUtility.GetAreaMultiplier.
            FP radius = Radius[index] * StatUtility.GetAreaMultiplier(f, filter.Entity);

            if (radius <= FP._0)
                return;

            Span<EntityRef> allies = stackalloc EntityRef[PlayerQueryUtility.MaxPlayerLayerCandidates];
            int alliesCount = EnemyMovementUtility.FindPlayersInRadiusIncludingDashing(f, filter.Transform3D->Position, radius, allies);

            for (int i = 0; i < alliesCount; i++)
            {
                EntityRef ally = allies[i];

                // Brute himself is deliberately NOT excluded - he trivially ends the dash within his
                // own radius, and at rank 1 that self-guard is the whole point of dashing defensively.
                if (f.Unsafe.TryGetPointer<StatusEffects>(ally, out var status) == false
                    || status->AllyGuardGrantCooldownRemaining > FP._0)
                    continue;

                status->AllyGuardGrantCooldownRemaining = CooldownPerAlly;

                StatusEffectUtility.ApplyFreeHitGuard(f, ally, filter.Entity, guardDuration);
            }
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }
    }
}
