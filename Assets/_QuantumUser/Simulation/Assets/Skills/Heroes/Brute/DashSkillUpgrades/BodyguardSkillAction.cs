namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Ranked Dash Ascension (Bodyguard) - allies near the dash destination get Shield back, growing
    // per rank; rank 3 additionally grants them a brief DR window (StatusEffectUtility.
    // ApplyTemporaryDamageReduction, the same shared reactive-proc slot Guardian's own rank 3 uses).
    //
    // Restores a FLAT amount, not a fraction of the ally's Max Shield. That's a deliberate change from
    // the earlier percentage version: a percentage restore scales with the recipient's own pool, so a
    // dash-cooldown build could pump unbounded effective Shield into a high-Shield teammate. A flat
    // number plus the per-ally cooldown below caps the sustain from both directions.
    //
    // The cooldown lives on the ALLY (StatusEffects.AllyShieldRestoreCooldownRemaining), not on Brute:
    // capping it per-Brute would still let two Brutes chain-refill one teammate, and it would punish a
    // Brute for dashing between different allies, which is exactly the play this line should reward.
    //
    // Brute himself is included in the ally scan (he trivially ends the dash within his own Radius of
    // himself) but only gets SelfEffectMultiplier of the full amount - a reduced, not full,
    // self-benefit, configurable rather than hardcoded.
    //
    // That self-include only actually works via FindPlayersInRadiusIncludingDashing, NOT the plain
    // FindPlayersInRadius every non-dash ally scan uses: this fires at dash END, and the broadphase it
    // queries was built by PhysicsSystem3D (which runs before every user system) while Brute was still
    // parked on the IgnoreProjectile layer for his dash i-frames. DashSkillData.End restores his layer
    // one line before this executes, but that is far too late to affect a broadphase already built this
    // tick - so a plain Player-mask query drops him 100% of the time, never intermittently. Allies were
    // always found correctly, which is exactly why this read as "Bodyguard doesn't shield me".
    public unsafe partial class BodyguardSkillAction : SkillActionData
    {
        public FP[] Radius = { FP._6, FP._8, FP._8 };

        [Tooltip("Flat Shield restored per affected ally, per rank. Deliberately not a percentage - see this class's own comment.")]
        public FP[] ShieldRestore = { 10, 15, 20 };

        [Tooltip("Per-ALLY cooldown before this can restore Shield to that same ally again.")]
        public FP CooldownPerAlly = FP.FromString("4.5");

        public FP SelfEffectMultiplier = FP._0_50;

        [Header("Rank 3")]
        public FP DamageReductionAmount = FP._0_20;
        public FP DamageReductionDuration = FP._2;

        public BodyguardSkillAction()
        {
            Phase = SkillActionPhase.End;
        }

        public override FP EffectRadius => Radius[Radius.Length - 1];

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase firedPhase, AssetRef<SkillActionData> selfRef)
        {
            int rank = System.Math.Max(1, SkillUpgradeUtility.GetRank(f, filter.Entity, selfRef));
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            FPVector3 position = filter.Transform3D->Position;
            var allies = EnemyMovementUtility.FindPlayersInRadiusIncludingDashing(f, position, Radius[index]);
            FP amount = ShieldRestore[index];

            for (int i = 0; i < allies.Count; i++)
            {
                EntityRef ally = allies[i].Entity;

                if (f.Unsafe.TryGetPointer<StatusEffects>(ally, out var status) == false
                    || status->AllyShieldRestoreCooldownRemaining > FP._0)
                    continue;

                status->AllyShieldRestoreCooldownRemaining = CooldownPerAlly;

                FP allyAmount = ally == filter.Entity ? amount * SelfEffectMultiplier : amount;

                if (f.Unsafe.TryGetPointer<Shield>(ally, out var shield) == true)
                {
                    ShieldUtility.ApplyFlatShield(f, ally, filter.Entity, shield, allyAmount);
                }

                if (rank >= 3)
                {
                    StatusEffectUtility.ApplyTemporaryDamageReduction(f, ally, DamageReductionDuration, DamageReductionAmount);
                }
            }
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }
    }
}
