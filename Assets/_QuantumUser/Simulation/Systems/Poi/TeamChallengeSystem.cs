namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Ticks each Optional Team Challenge POI's own WaitingForTeam/Starting/ChallengeActive state
    // (see TeamChallenge.qtn/TeamChallengeUtility). Lives inside GameplaySystemGroup (unlike
    // BossPauseSystem/ChestSystem/LevelUpSystem) since it never itself disables/enables that group -
    // players keep fighting normally throughout WaitingForTeam AND ChallengeActive; its own pause is
    // Global.ActiveTeamChallengeCount, checked from CombatDirectorSystem/SurvivalProgressionUtility
    // much earlier in the same system list, same mechanism TraversalChallengeSystem's own pause uses.
    [Preserve]
    public unsafe class TeamChallengeSystem : SystemMainThreadFilter<TeamChallengeSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            switch (filter.TeamChallenge->State)
            {
                // Rolled here, on the very first tick this entity is ticked while Available - long
                // before any player could physically reach it and press Interact - so
                // InteractionPromptWidget's own Available-state description can already show the
                // real ChallengeDefinition.Description instead of a generic placeholder. See
                // TeamChallengeUtility.EnsureChallengeRolled's own comment.
                case TeamChallengeState.Available:
                    TeamChallengeUtility.EnsureChallengeRolled(f, filter.Entity, filter.TeamChallenge);
                    break;

                case TeamChallengeState.WaitingForTeam:
                    TickWaitingForTeam(f, filter.Entity, filter.TeamChallenge, filter.Transform3D);
                    break;

                case TeamChallengeState.Starting:
                    TickStarting(f, filter.Entity, filter.TeamChallenge);
                    break;

                case TeamChallengeState.ChallengeActive:
                    TickChallengeActive(f, filter.Entity, filter.TeamChallenge);
                    break;
            }
        }

        // Cancels any Ready marker whose holder has left the Ready/Cancel Area - no timeout, no
        // toast (the X/Y READY HUD counter on TeamChallengeView communicates this, per docs' own
        // "no spam" requirement). Then re-checks unanimity every tick (not just on a fresh Ready
        // press) - see TeamChallengeUtility.TryBeginStarting's own comment for why (a disconnect
        // shrinking the requirement must also be able to complete unanimity with no new press).
        private static void TickWaitingForTeam(Frame f, EntityRef poi, TeamChallenge* challenge, Transform3D* poiTransform)
        {
            TeamChallengeConfig config = f.FindAsset(challenge->Config);
            FP radiusSqr = config.ReadyCancelRadius * config.ReadyCancelRadius;

            List<EntityRef> toCancel = new List<EntityRef>();
            var filtered = f.Filter<TeamChallengeReady, Transform3D>();

            while (filtered.Next(out EntityRef entity, out TeamChallengeReady ready, out Transform3D readyTransform))
            {
                if (ready.Poi != poi)
                    continue;

                if (EnemyMovementUtility.FlatSqrDistance(poiTransform->Position, readyTransform.Position) > radiusSqr)
                    toCancel.Add(entity);
            }

            for (int i = 0; i < toCancel.Count; i++)
            {
                f.Remove<TeamChallengeReady>(toCancel[i]);
            }

            // After the cancel sweep, not before - a bot immediately re-Readying here always wins
            // out over that same tick's distance check, since it has nobody at the keyboard to walk
            // it back into the Ready/Cancel Area anyway (see TeamChallengeUtility.AutoReadyBots).
            TeamChallengeUtility.AutoReadyBots(f, poi);

            TeamChallengeUtility.TryBeginStarting(f, poi, challenge);
        }

        private static void TickStarting(Frame f, EntityRef poi, TeamChallenge* challenge)
        {
            f.Global->TeamChallengeTimeRemaining = challenge->RemainingCountdown;

            challenge->RemainingCountdown -= f.DeltaTime;

            if (challenge->RemainingCountdown <= FP._0)
            {
                TeamChallengeUtility.BeginChallengeActive(f, poi, challenge);
            }
        }

        private static void TickChallengeActive(Frame f, EntityRef poi, TeamChallenge* challenge)
        {
            // Safety net for an unexpected interruption (the run itself ending mid-challenge) -
            // TeamChallengeUtility.End is the single funnel every exit path (including this one)
            // calls through, so Cursed Survival's curse is always restored regardless of how the
            // attempt actually ends. GameplaySystemGroup (where this system lives) is never disabled
            // on RunFailed/Victory, so this is guaranteed to observe it within one tick.
            if (f.Global->CurrentState == GameState.RunFailed || f.Global->CurrentState == GameState.Victory)
            {
                TeamChallengeUtility.End(f, poi, challenge, success: false);
                return;
            }

            f.Global->TeamChallengeTimeRemaining = challenge->RemainingChallengeTime;

            ChallengeDefinition definition = f.FindAsset(challenge->SelectedChallenge);

            if (definition == null)
                return; // already logged loud by BeginChallengeActive if this ever happens

            TeamChallengeUtility.PulseChallengeEncounter(f, poi, challenge, definition);
            ChallengeObjectiveUtility.Tick(f, poi, challenge, definition);
        }

        public struct Filter
        {
            public EntityRef Entity;
            public TeamChallenge* TeamChallenge;
            public Transform3D* Transform3D;
        }
    }
}
