namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Reaction half of Optional Team Challenge's generic runtime - Kill Rush/Flawless Hunt's shared
    // team kill counter, and Flawless Hunt's real-HP-loss failure. Mirrors RiftMutationReactionSystem's
    // shape (a single system dispatching off signals, unfiltered SystemMainThread, no per-tick Update
    // work of its own).
    [Preserve]
    public unsafe class TeamChallengeReactionSystem : SystemMainThread, ISignalOnEntityKilled, ISignalOnHealthDamageApplied
    {
        public override void Update(Frame f)
        {
        }

        // Kill Rush/Flawless Hunt's shared team counter - only counts a kill tagged TeamChallengeSpawn
        // (i.e. spawned FOR this specific challenge attempt - see TeamChallengeUtility.
        // BeginChallengeActive) against a currently ChallengeActive attempt of a matching Type.
        public void OnEntityKilled(Frame f, EntityRef target, EntityRef owner, DamageSource source)
        {
            if (f.Unsafe.TryGetPointer<TeamChallengeSpawn>(target, out var spawn) == false)
                return;

            if (f.Unsafe.TryGetPointer<TeamChallenge>(spawn->Poi, out var challenge) == false
                || challenge->State != TeamChallengeState.ChallengeActive)
                return;

            ChallengeDefinition definition = f.FindAsset(challenge->SelectedChallenge);

            if (definition == null || (definition.Type != ChallengeType.KillRush && definition.Type != ChallengeType.FlawlessHunt))
                return;

            challenge->KillCount++;

            if (challenge->KillCount >= challenge->KillTarget)
            {
                TeamChallengeUtility.End(f, spawn->Poi, challenge, success: true);
            }
        }

        // Flawless Hunt - fails the instant ANY participant loses real Health. This signal fires
        // ONLY after Accessory-block/Free-Hit-Guard/Shield-absorb early-returns in
        // DamageUtility.ApplyDamage, so a blocked/absorbed hit never reaches here - exactly the
        // "blocked hits don't fail it, only actual HP loss does" rule, with no extra detection
        // logic required.
        public void OnHealthDamageApplied(Frame f, EntityRef target, EntityRef owner, FP amount, DamageSource source, QBoolean directHit)
        {
            if (f.Unsafe.TryGetPointer<TeamChallengeParticipant>(target, out var participant) == false)
                return;

            if (f.Unsafe.TryGetPointer<TeamChallenge>(participant->Poi, out var challenge) == false
                || challenge->State != TeamChallengeState.ChallengeActive)
                return;

            ChallengeDefinition definition = f.FindAsset(challenge->SelectedChallenge);

            if (definition == null || definition.Type != ChallengeType.FlawlessHunt)
                return;

            TeamChallengeUtility.End(f, participant->Poi, challenge, success: false);
        }
    }
}
