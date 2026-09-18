namespace Quantum
{
    using Photon.Deterministic;

    // Per-tick objective evaluation for whichever ChallengeType is currently ChallengeActive - the
    // "generic challenge runtime" evaluator TeamChallengeSystem calls into (see TeamChallenge.qtn).
    // Kill Rush/Flawless Hunt's own SUCCESS (KillCount >= KillTarget) is evaluated reactively by
    // TeamChallengeReactionSystem.OnEntityKilled instead, not here - this owns the generic timeout
    // rule (any Type) plus Cursed Survival's own timer/incapacitation poll.
    public static unsafe class ChallengeObjectiveUtility
    {
        public static void Tick(Frame f, EntityRef poi, TeamChallenge* challenge, ChallengeDefinition definition)
        {
            if (definition.Type == ChallengeType.CursedSurvival)
            {
                TickCursedSurvival(f, poi, challenge, definition);
                return;
            }

            TickTimeout(f, poi, challenge, definition);
        }

        // Generic timeout rule - Kill Rush's own failure (ran out of time before KillTarget). A
        // no-op for any Type authored with Duration <= 0 (Flawless Hunt doesn't require one - it
        // fails immediately on real HP loss instead, see TeamChallengeReactionSystem).
        private static void TickTimeout(Frame f, EntityRef poi, TeamChallenge* challenge, ChallengeDefinition definition)
        {
            if (definition.Duration <= FP._0)
                return;

            challenge->RemainingChallengeTime -= f.DeltaTime;

            if (challenge->RemainingChallengeTime <= FP._0)
            {
                TeamChallengeUtility.End(f, poi, challenge, success: false);
            }
        }

        // Cursed Survival - survive Duration with every participant still Alive. Poll-driven
        // (no "just entered Downed" signal exists in this codebase) - same continuous-check idiom
        // SurvivalProgressionUtility/TraversalChallengeSystem already use for their own live checks,
        // rather than manufacturing a new signal solely for this.
        private static void TickCursedSurvival(Frame f, EntityRef poi, TeamChallenge* challenge, ChallengeDefinition definition)
        {
            var filtered = f.Filter<TeamChallengeParticipant>();

            while (filtered.Next(out EntityRef entity, out TeamChallengeParticipant participant))
            {
                if (participant.Poi != poi)
                    continue;

                if (PlayerLifeStateUtility.IsIncapacitated(f, entity) == true)
                {
                    TeamChallengeUtility.End(f, poi, challenge, success: false);
                    return;
                }
            }

            if (definition.Duration <= FP._0)
                return; // authored with no Duration - an authoring mistake, not handled specially; this attempt simply never times out

            challenge->RemainingChallengeTime -= f.DeltaTime;

            if (challenge->RemainingChallengeTime <= FP._0)
            {
                TeamChallengeUtility.End(f, poi, challenge, success: true);
            }
        }
    }
}
