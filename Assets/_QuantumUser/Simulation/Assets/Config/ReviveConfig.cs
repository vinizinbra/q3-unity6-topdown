namespace Quantum
{
    using Photon.Deterministic;

    // Hold-to-revive's own tuning - see ReviveUtility/PlayerLifeStateUtility/ReviveChannelSystem
    // and docs/revive.md. Single canonical source every consumer reads from (both the life-state
    // side - bleed-out, completion heal/invuln - and the interaction/channel side - durations,
    // move-speed multiplier, damage-pause), so nothing duplicates these values. No KOReviveDuration
    // anymore - KO has no revive path at all (teammate or self), it's a dead end until
    // Global.BreathingAreaSecured auto-revives everyone still incapacitated - confirmed with the
    // user.
    public class ReviveConfig : AssetObject
    {
        public FP DownedReviveDuration = (FP._2 + FP._0_50);
        public FP DownedBleedOutDuration = 20;
        public FP ReviveMoveSpeedMultiplier = FP.FromString("0.30");

        // How many seconds of banked ReviveProgress decay per real second while nobody is actively
        // channeling it (see PlayerLifeStateSystem) - drifting out of range (e.g. knockback)
        // INTERRUPTS a hold (ReviveChannelSystem); taking damage alone does not. The
        // interrupted progress isn't lost, but the progress itself isn't lost - it just isn't held forever either.
        // 0.5 - half the rate progress builds at - means an interrupted hold stays roughly half-banked
        // for as long as it took to build, giving a teammate resuming it a real head start without
        // making the timer meaningless.
        public FP ReviveProgressDecayRate = FP._0_50;

        public FP ReviveHealthPercent = FP.FromString("0.40");
        public FP ReviveInvulnerabilityDuration = 1;
        public FP ReviveInteractionRange = 3;

        // Brief grace window for the teammate who just completed a revive hold (the revived player
        // gets ReviveInvulnerabilityDuration above).
        public FP ReviverInvulnerabilityDuration = 1;

        // Grace window for every player once a level-up/chest pick (upgrade or weapon) resolves and
        // gameplay resumes - see LevelUpUtility.Resolve.
        public FP PostChoiceInvulnerabilityDuration = FP._0_50;
    }
}
