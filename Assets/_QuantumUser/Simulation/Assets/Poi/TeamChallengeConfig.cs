namespace Quantum
{
    using Photon.Deterministic;

    // Per-POI-instance tuning for Optional Team Challenge (see TeamChallenge.qtn). The small
    // Interaction Area radius is NOT duplicated here - it's the sibling Interactable.Radius field
    // already authored on this POI's own EntityPrototype, same convention every other POI kind uses.
    public class TeamChallengeConfig : AssetObject
    {
        // Which ChallengeDefinition is rolled (deterministically, via f.RNG) the instant the first
        // Raider Readies up - see TeamChallengeUtility.TryReadyUp.
        public AssetRef<ChallengeDefinition>[] ChallengePool;

        // The larger Ready/Cancel Area radius a Readied Raider must stay inside - suggested ~2.5x
        // the sibling Interactable.Radius, author's choice per POI instance.
        public FP ReadyCancelRadius;

        // Seconds of Starting-state countdown once unanimous Ready is reached, before
        // ChallengeActive begins. 0 skips the countdown entirely.
        public FP CountdownDuration;
    }
}
