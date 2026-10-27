namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;
    using UnityEngine;

    // Dash line 10 - the dash marks enemies and feeds Vendetta and Overdrive (deals no damage itself):
    //  - Rank 1: every enemy the dash passes through is marked by Vendetta (the one deliberate way to
    //    mark proactively - see MaxVendettaSystem's comment on why weapon hits don't).
    //  - Rank 2: a dash that passes through at least one enemy hands back the charge it spent
    //    (TryRefundDash, at End - before SkillSystem.FinishSkill arms that charge's cooldown), so
    //    Max can keep chaining dashes through a pack.
    //  - Rank 3: each enemy hit cuts the Hero Skill's cooldown if Overdrive is dormant, or extends an
    //    active Overdrive, by RewardPerHit - at most MaxRewardPerDash per dash. The extension goes
    //    through OverdriveUtility.TryExtend, so it also books against the shared per-activation
    //    OverdriveExtension ledger Uncontrolled Fury uses.
    // Procs at most once per enemy per dash - see VendettaStrikeHitTracker, granted fresh on this
    // action's own Begin phase, same shape/precedent as Brute's IronShoulderSkillAction/
    // IronShoulderHitTracker. Enemy sweep shape copied from Pixie's SlowFuseSkillAction.
    public unsafe partial class VendettaStrikeSkillAction : SkillActionData
    {
        public FP Radius = FP._1_50;

        [Header("Rank 3")]
        public FP RewardPerHit = FP._0_50;
        public FP MaxRewardPerDash = 3;

        private const int MaxTrackedHits = 8;

        public VendettaStrikeSkillAction()
        {
            Phase = SkillActionPhase.Begin | SkillActionPhase.OnGoing | SkillActionPhase.End;
            Interval = 0;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase firedPhase, AssetRef<SkillActionData> selfRef)
        {
            if (firedPhase == SkillActionPhase.Begin)
            {
                f.AddOrGet<VendettaStrikeHitTracker>(filter.Entity, out var tracker);
                tracker->HitCount = 0;
                tracker->DashRefundEarned = false;
                return;
            }

            int rank = System.Math.Max(1, SkillUpgradeUtility.GetRank(f, filter.Entity, selfRef));

            if (firedPhase == SkillActionPhase.End)
            {
                if (rank >= 2)
                {
                    TryRefundDash(f, filter.Entity, slot);
                }

                return;
            }

            if (f.Unsafe.TryGetPointer<RevengeConfig>(filter.Entity, out var config) == false)
                return;

            // Skill Area - see StatUtility.GetAreaMultiplier.
            Shape3D sphere = Shape3D.CreateSphere(Radius * StatUtility.GetAreaMultiplier(f, filter.Entity));
            var hits = f.Physics3D.OverlapShape(filter.Transform3D->Position, FPQuaternion.Identity, sphere, EnemyMovementUtility.GetEnemyLayerMask(f), QueryOptions.HitAll);

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef target = hits[i].Entity;

                if (f.Has<Enemy>(target) == false || f.Has<Invulnerable>(target) == true)
                    continue;

                if (TryMarkProcced(f, filter.Entity, target) == false)
                    continue;

                if (rank >= 2 && f.Unsafe.TryGetPointer<VendettaStrikeHitTracker>(filter.Entity, out var tracker) == true)
                {
                    tracker->DashRefundEarned = true;
                }

                MaxVendettaSystem.ApplyMark(f, filter.Entity, target, config);

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
        // this action has nothing else to gate per hit, so a dedupe here covers the mark and
        // rank-3 reward all at once. Once the tracker's capacity is full, any further new enemy just
        // isn't deduped (falls back to repeat-every-tick behavior).
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

        // Rank 2 - runs at End, before SkillSystem.FinishSkill arms the cooldown for the charge this
        // dash spent: putting the charge straight back means FinishSkill finds nothing to recover
        // (or keeps recovering whatever other charge already was). Capped at the effective max, so
        // it respects Dead Weight's hard cap.
        private static void TryRefundDash(Frame f, EntityRef entity, SkillSlot* slot)
        {
            if (f.Unsafe.TryGetPointer<VendettaStrikeHitTracker>(entity, out var tracker) == false || tracker->DashRefundEarned == false)
                return;

            tracker->DashRefundEarned = false;

            if (slot->CurrentStacks < SkillSystem.ResolveEffectiveMaxStacks(f, entity, SkillSlotId.DashSkill, slot))
            {
                slot->CurrentStacks++;
            }
        }

        // Rank 3 - Overdrive's own current state decides the reward: RageOverdrive only exists while
        // an activation is actually running (same presence-as-state-check idiom every other Overdrive
        // Ascension here uses), so its absence means dormant. Capped per dash off the hit tracker's
        // own count (this hit is already in it).
        private void TryRewardOverdrive(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<VendettaStrikeHitTracker>(entity, out var tracker) == true
                && (FP)tracker->HitCount * RewardPerHit > MaxRewardPerDash)
                return;

            if (f.Has<RageOverdrive>(entity) == true)
            {
                OverdriveUtility.TryExtend(f, entity, RewardPerHit);
                return;
            }

            if (f.Unsafe.TryGetPointer<CharacterSkills>(entity, out var skills) == true)
            {
                SkillSystem.ReduceCooldown(f, skills, SkillSlotId.HeroSkill, RewardPerHit);
            }
        }
    }
}
