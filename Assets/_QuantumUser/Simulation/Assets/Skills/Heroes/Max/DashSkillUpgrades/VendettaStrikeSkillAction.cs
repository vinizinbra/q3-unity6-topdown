namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;
    using UnityEngine;

    // Dash line 10 - each rank unlocks a genuinely new effect on top of the last, rather than scaling
    // the same numbers:
    //  - Rank 1: applies Burn to any enemy caught in the dash. No Vendetta mark yet - Burn only, also
    //    granting CanApplyBurn so Flashpoint becomes eligible.
    //  - Rank 2: also creates/refreshes a RevengeMark on that enemy, even one that's never damaged Max
    //    (unlike the base Vendetta passive, which only ever marks reactively - see MaxVendettaSystem).
    //    Deliberate: this is a picked Ascension, not automatic base-passive behavior, so gating
    //    proactive marking behind it (rather than making every weapon hit mark unconditionally, which
    //    was tried and reverted - see MaxVendettaSystem's own comment) is what makes it a real choice.
    //    Worth taking even on a fresh, unmarked enemy purely for the base Vendetta bonus damage the
    //    mark grants plus the guaranteed RevengeConfig.MinHealFraction/EnemyMaxHealthFraction floor on
    //    a kill, even with zero StoredDamage banked.
    //  - Rank 3: also rewards landing the strike depending on Overdrive's own current state - reduces
    //    the Hero Skill's own cooldown if dormant, or extends the current Overdrive activation if
    //    already active. That extension goes through OverdriveUtility.TryExtend like every other
    //    source, so it books against - and is capped by - the same shared per-activation
    //    OverdriveExtension ledger Uncontrolled Fury uses. There is deliberately no way for this to
    //    add duration past that ceiling, no matter how many enemies a dash sweeps.
    // Procs at most once per enemy per dash - see VendettaStrikeHitTracker, granted fresh on this
    // action's own Begin phase, same shape/precedent as Brute's IronShoulderSkillAction/
    // IronShoulderHitTracker. Enemy sweep shape copied from Pixie's SlowFuseSkillAction.
    public unsafe partial class VendettaStrikeSkillAction : SkillActionData
    {
        public FP Radius = FP._1_50;
        public FP BurnDuration = 3;
        public FP BurnIntensity = FP._0_10;

        [Header("Rank 3")]
        public FP CooldownReduction = 2;
        public FP OverdriveDurationBonus = 1;

        private const int MaxTrackedHits = 8;

        public VendettaStrikeSkillAction()
        {
            Phase = SkillActionPhase.Begin | SkillActionPhase.OnGoing;
            Interval = 0;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase firedPhase, AssetRef<SkillActionData> selfRef)
        {
            if (firedPhase == SkillActionPhase.Begin)
            {
                f.AddOrGet<VendettaStrikeHitTracker>(filter.Entity, out var tracker);
                tracker->HitCount = 0;
                return;
            }

            EffectConfig effectConfig = StatusEffectUtility.GetEffectConfig(f);

            if (effectConfig == null)
                return;

            int rank = System.Math.Max(1, SkillUpgradeUtility.GetRank(f, filter.Entity, selfRef));

            // Skill Area - see StatUtility.GetAreaMultiplier.
            Shape3D sphere = Shape3D.CreateSphere(Radius * StatUtility.GetAreaMultiplier(f, filter.Entity));
            var hits = f.Physics3D.OverlapShape(filter.Transform3D->Position, FPQuaternion.Identity, sphere, -1, QueryOptions.HitAll);

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef target = hits[i].Entity;

                if (f.Has<Enemy>(target) == false || f.Has<Invulnerable>(target) == true)
                    continue;

                if (TryMarkProcced(f, filter.Entity, target) == false)
                    continue;

                StatusEffectUtility.ApplyBurn(f, target, BurnDuration, BurnIntensity, filter.Entity, DamageSource.Skill, effectConfig.TickInterval);
                f.AddOrGet<CanApplyBurn>(filter.Entity, out _);

                if (rank >= 2 && f.Unsafe.TryGetPointer<RevengeConfig>(filter.Entity, out var config) == true)
                {
                    f.AddOrGet<RevengeMark>(target, out var mark);

                    if (mark->MarkedBy != filter.Entity)
                    {
                        // A different Vendetta holder (or no holder yet) claims this enemy - any
                        // previously stored damage belonged to someone else and is discarded.
                        mark->MarkedBy = filter.Entity;
                        mark->StoredDamage = FP._0;
                    }

                    mark->RemainingDuration = config->MarkDuration;
                }

                if (rank >= 3)
                {
                    TryRewardOverdrive(f, filter.Entity);
                }
            }
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }

        // False if target already procced earlier this activation (see VendettaStrikeHitTracker) -
        // this action has nothing else to gate per hit, so a dedupe here covers the Burn, mark
        // refresh and rank-3 reward all at once. Once the tracker's capacity is full, any further new
        // enemy just isn't deduped (falls back to repeat-every-tick behavior).
        private static bool TryMarkProcced(Frame f, EntityRef caster, EntityRef target)
        {
            if (f.Unsafe.TryGetPointer<VendettaStrikeHitTracker>(caster, out var tracker) == false)
                return true;

            for (int i = 0; i < tracker->HitCount; i++)
            {
                if (tracker->HitEntities[i] == target)
                    return false;
            }

            if (tracker->HitCount < MaxTrackedHits)
            {
                tracker->HitEntities[tracker->HitCount] = target;
                tracker->HitCount++;
            }

            return true;
        }

        // Rank 3 - Overdrive's own current state decides the reward: RageOverdrive only exists while
        // an activation is actually running (same presence-as-state-check idiom every other Overdrive
        // Ascension here uses), so its absence means dormant.
        private void TryRewardOverdrive(Frame f, EntityRef entity)
        {
            if (f.Has<RageOverdrive>(entity) == true)
            {
                OverdriveUtility.TryExtend(f, entity, OverdriveDurationBonus);
                return;
            }

            if (f.Unsafe.TryGetPointer<CharacterSkills>(entity, out var skills) == true)
            {
                SkillSystem.ReduceCooldown(f, skills, SkillSlotId.HeroSkill, CooldownReduction);
            }
        }
    }
}
