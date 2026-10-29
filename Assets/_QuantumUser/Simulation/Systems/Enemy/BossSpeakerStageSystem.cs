namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Drives a running "Som no Talo" sequence (BossSpeakerStage on the boss, see
    // SpeakerStageDeliveryData) - the boss itself keeps acting normally the whole time:
    //
    // - every EmitInterval it forces the next living speaker's own BasicAction (the gap ring) through
    //   its normal Preparation -> Telegraph -> Begin, alternating between the two, so each emission
    //   gets the speaker's real windup as its warning;
    // - a destroyed speaker simply drops out of the rotation (its future emissions never happen);
    // - both destroyed: the sequence ends and the boss is forced into SpeakerStageDeliveryData.
    //   ExposedAction (deferred if mid-leap) - the reward;
    // - MaxDuration reached: the remaining speakers pop and the sequence ends with NO exposure, and
    //   the optional ClosingAction's cooldown is cleared;
    // - boss dead/gone: the speakers pop (orphan sweep below), no reward.
    //
    // Registered right after BossSystem (same "act on this tick's resolved boss state" placement).
    [Preserve]
    public unsafe class BossSpeakerStageSystem : SystemMainThread
    {
        public override void Update(Frame f)
        {
            List<EntityRef> finished = null;

            var stages = f.Filter<Enemy, BossSpeakerStage>();
            while (stages.NextUnsafe(out EntityRef boss, out Enemy* enemy, out BossSpeakerStage* stage))
            {
                if (TickStage(f, boss, enemy, stage) == false)
                {
                    finished ??= new List<EntityRef>();
                    finished.Add(boss);
                }
            }

            if (finished != null)
            {
                for (int i = 0; i < finished.Count; i++)
                    f.Remove<BossSpeakerStage>(finished[i]);
            }

            SweepOrphanSpeakers(f);
        }

        // Returns false once the sequence is over (caller removes the component afterwards - never
        // mid-filter).
        private static bool TickStage(Frame f, EntityRef boss, Enemy* enemy, BossSpeakerStage* stage)
        {
            SpeakerStageDeliveryData config = f.FindAsset(stage->Config);

            if (config == null || enemy->Phase == EnemyActionPhase.Dead)
            {
                PopSpeakers(f, stage);
                return false;
            }

            int alive = 0;
            for (int i = 0; i < stage->Speakers.Length; i++)
            {
                if (IsSpeakerAlive(f, stage->Speakers[i]) == true)
                    alive++;
            }

            if (alive == 0)
            {
                // Both destroyed by the players - the exposure window is the reward.
                FPVector3 position = f.Unsafe.TryGetPointer<Transform3D>(boss, out var transform) ? transform->Position : default;
                f.Events.BossSpeakerStageBroken(boss, position);

                if (config.ExposedAction.IsValid == true)
                    BossSystem.TryForceAction(f, boss, config.ExposedAction);

                Log.Debug($"[Boss] {boss} Som no Talo broken - both speakers destroyed, exposing the boss");
                return false;
            }

            stage->TimeRemaining -= f.DeltaTime;

            if (stage->TimeRemaining <= FP._0)
            {
                PopSpeakers(f, stage);
                ClearClosingActionCooldown(f, boss, enemy, config);
                Log.Debug($"[Boss] {boss} Som no Talo expired with {alive} speaker(s) still up - popped, no exposure");
                return false;
            }

            stage->EmitTimer -= f.DeltaTime;

            if (stage->EmitTimer > FP._0)
                return true;

            // Alternate; skip a destroyed speaker so the survivor keeps the rhythm alone.
            int index = stage->NextSpeaker % stage->Speakers.Length;

            if (IsSpeakerAlive(f, stage->Speakers[index]) == false)
                index = (index + 1) % stage->Speakers.Length;

            // A speaker still mid-windup/staggered just waits a tick - the timer stays expired, so it
            // fires as soon as it can rather than skipping its turn.
            if (TryForceEmission(f, stage->Speakers[index], enemy->Target) == false)
                return true;

            stage->NextSpeaker = (byte)((index + 1) % stage->Speakers.Length);
            stage->EmitTimer = config.EmitInterval / BossPhaseUtility.ResolveRecoveryMultiplier(f, boss);
            return true;
        }

        private static bool IsSpeakerAlive(Frame f, EntityRef speaker)
        {
            return speaker != EntityRef.None
                && f.Unsafe.TryGetPointer<Enemy>(speaker, out var enemy) == true
                && enemy->Phase != EnemyActionPhase.Dead;
        }

        // Same direct Phase/StateTimer write BossSystem uses for forced boss actions, on the speaker's
        // BasicAction (slot 0). Only from a resting phase - never on top of a windup in progress.
        private static bool TryForceEmission(Frame f, EntityRef speaker, EntityRef bossTarget)
        {
            if (f.Unsafe.TryGetPointer<Enemy>(speaker, out var enemy) == false || f.Unsafe.TryGetPointer<Transform3D>(speaker, out var transform) == false)
                return false;

            if (enemy->Phase != EnemyActionPhase.Idle && enemy->Phase != EnemyActionPhase.Chasing && enemy->Phase != EnemyActionPhase.Recovery)
                return false;

            EnemyActionData action = EnemyDecisionUtility.ResolveAction(f, f.FindAsset(enemy->EnemyData), 0);

            if (action == null)
                return false;

            // The ring's opening is placed relative to the target - hand the speaker the boss's.
            if (bossTarget != EntityRef.None)
                enemy->Target = bossTarget;

            enemy->CurrentActionSlot = 0;
            enemy->StateTimer = action.AnticipationTime;
            enemy->Phase = EnemyActionPhase.Preparation;
            enemy->SkillStartPosition = transform->Position;
            enemy->SkillTargetPosition = transform->Position;
            return true;
        }

        private static void PopSpeakers(Frame f, BossSpeakerStage* stage)
        {
            for (int i = 0; i < stage->Speakers.Length; i++)
            {
                PopSpeaker(f, stage->Speakers[i]);
                stage->Speakers[i] = EntityRef.None;
            }
        }

        // Same "pop, no drops/credit" removal BossDefeatUtility.ClearArena uses.
        private static void PopSpeaker(Frame f, EntityRef speaker)
        {
            if (speaker == EntityRef.None || f.Unsafe.TryGetPointer<Enemy>(speaker, out var enemy) == false)
                return;

            if (enemy->Phase != EnemyActionPhase.Dead)
                DamageUtility.FireEnemyExploded(f, speaker, enemy->EnemyData);

            f.Destroy(speaker);
        }

        private static void ClearClosingActionCooldown(Frame f, EntityRef boss, Enemy* enemy, SpeakerStageDeliveryData config)
        {
            if (config.ClosingAction.IsValid == false)
                return;

            EnemyDataAsset data = f.FindAsset(enemy->EnemyData);

            for (int i = 0; i < data.Actions.SkillActions.Count; i++)
            {
                if (data.Actions.SkillActions[i] == config.ClosingAction)
                {
                    EnemyDecisionUtility.SetCooldownRemaining(f, boss, enemy, i + 1, FP._0);
                    return;
                }
            }
        }

        // Any speaker whose owner is gone, dead, or no longer running a sequence that lists it - boss
        // killed (the defeat cinematic's ClearArena usually beats this to it), debug-despawned, etc.
        private static void SweepOrphanSpeakers(Frame f)
        {
            List<EntityRef> orphans = null;

            var speakers = f.Filter<BossSpeaker>();
            while (speakers.NextUnsafe(out EntityRef speaker, out BossSpeaker* link))
            {
                if (IsOwnedByLiveStage(f, speaker, link->Owner) == true)
                    continue;

                // Already dying from the players' damage - its own death pipeline removes it.
                if (f.Unsafe.TryGetPointer<Enemy>(speaker, out var speakerEnemy) == true && speakerEnemy->Phase == EnemyActionPhase.Dead)
                    continue;

                orphans ??= new List<EntityRef>();
                orphans.Add(speaker);
            }

            if (orphans == null)
                return;

            for (int i = 0; i < orphans.Count; i++)
                PopSpeaker(f, orphans[i]);
        }

        private static bool IsOwnedByLiveStage(Frame f, EntityRef speaker, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<Enemy>(owner, out var ownerEnemy) == false || ownerEnemy->Phase == EnemyActionPhase.Dead)
                return false;

            if (f.Unsafe.TryGetPointer<BossSpeakerStage>(owner, out var stage) == false)
                return false;

            for (int i = 0; i < stage->Speakers.Length; i++)
            {
                if (stage->Speakers[i] == speaker)
                    return true;
            }

            return false;
        }
    }
}
