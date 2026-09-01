namespace Quantum
{
    using Photon.Deterministic;

    // The single reader of every run-wide encounter modifier (Frame.Global, see RunMutations.qtn) -
    // nothing else touches those fields directly. Written only by Run-scope Rift Mutations
    // (Greed/Overpopulation/Elite Territory/Blood Tithe/Escalation, see
    // docs/rift-mutations.md), but deliberately mutation-agnostic: any future run modifier - a
    // difficulty setting, a world event, a Cursed Rift outcome - can write the same fields and
    // every consumer below picks it up for free.
    //
    // Every field is a BONUS defaulting to 0, so an untouched run returns exactly 1 from all of
    // these and the Director/enemy/economy code paths behave bit-for-bit as they did before this
    // layer existed.
    public static unsafe class EncounterModifierUtility
    {
        // Baked into Health.MaxHealth once per spawn by EnemySystem.SeedHealth - already-alive
        // enemies keep whatever they rolled, which is the pre-existing behaviour of the Greed field
        // this replaced, not a new limitation.
        //
        // A NEGATIVE total is ignored for EnemyTier.Boss: a horde mutation (Overpopulation trades
        // -25% enemy HP for +40% density) should not quietly halve a boss's health bar, and the
        // design brief calls this out explicitly. A POSITIVE total still applies to every tier, so
        // Greed's +50% does reach a boss.
        public static FP ResolveEnemyHealthMultiplier(Frame f, EnemyTier tier)
        {
            FP bonus = f.Global->EnemyMaxHealthBonus;

            if (tier == EnemyTier.Boss && bonus < FP._0)
                return FP._1;

            return FPMath.Max(FP._0, FP._1 + bonus);
        }

        // Read LIVE (every hit) rather than baked at spawn, so picking Blood Tithe mid-fight makes
        // the enemies already on screen hit harder - the reading a "run-wide" modifier invites.
        // Applied in HitEffectUtility.ScaleByEnemyDamageMultiplier/ApplyDamageInRadius, the funnels
        // every enemy delivery type ultimately goes through.
        public static FP ResolveEnemyDamageMultiplier(Frame f)
        {
            return FPMath.Max(FP._0, FP._1 + f.Global->EnemyDamageBonus);
        }

        // How much more (or less) the Director should be spawning right now. Applied to all THREE
        // of its levers at once - budget accrual, MaxAliveEnemies and TargetPressure - exactly the
        // way the pre-existing SplitThreatMultiplier already is, because scaling only one of them
        // just moves the bottleneck to the other two.
        //
        // Composes a flat run-wide term (Overpopulation/Elite Territory) with Escalation's
        // within-phase ramp.
        public static FP ResolveSpawnDensityMultiplier(Frame f)
        {
            FP density = FPMath.Max(FP._0, FP._1 + f.Global->EnemySpawnDensityBonus);

            return density * ResolvePhaseRamp(f);
        }

        // Escalation - 1.0 at the start of a combat phase climbing to 1 + EscalationEndBonus
        // by its end, then back to 1.0 when the next one begins.
        //
        // Derived from the phase's own normalized progress (PhaseTimer / Duration) rather than a
        // timer of its own: that's deterministic by construction, and it resets for free because
        // SurvivalProgressionUtility.Tick already zeroes PhaseTimer on every transition. Clamp01
        // matters for the LAST phase, which never expires - its PhaseTimer runs past Duration
        // forever (see SurvivalConfig's own comment), and without the clamp the ramp would climb
        // without bound.
        //
        // Breathing and Boss phases are excluded outright, per the brief: a Break has no spawning
        // to escalate, and a boss encounter stops Director pulses entirely anyway.
        public static FP ResolvePhaseRamp(Frame f)
        {
            FP endBonus = f.Global->EscalationEndBonus;

            if (endBonus <= FP._0)
                return FP._1;

            SurvivalConfig survival = f.FindAsset(f.RuntimeConfig.SurvivalConfig);

            if (survival == null || survival.Phases == null || survival.Phases.Length == 0)
                return FP._1;

            int index = f.Global->CurrentPhaseIndex;

            if (index < 0 || index >= survival.Phases.Length)
                return FP._1;

            SurvivalPhase phase = survival.Phases[index];

            if (phase.Kind != SurvivalPhaseKind.Combat && phase.Kind != SurvivalPhaseKind.Elite)
                return FP._1;

            if (phase.Duration <= FP._0)
                return FP._1;

            FP progress = FPMath.Clamp01(f.Global->PhaseTimer / phase.Duration);

            return FP._1 + endBonus * progress;
        }

        // Elite Territory - biases the Director's own weighted group roll toward groups containing
        // an Elite-or-higher member, rather than substituting enemy types after the fact. Boss
        // spawning is untouched: a Boss phase never pulses at all (CombatDirectorSystem gates on
        // GameState.Boss), so the only thing this can ever reach is a normal combat roll.
        public static FP ResolveGroupWeightMultiplier(Frame f, EnemyGroupConfig group)
        {
            if (group == null)
                return FP._1;

            return ResolveWeightMultiplier(f, CombatDirectorUtility.GroupContainsMajor(f, group));
        }

        // SurvivalPhase.AllowedEnemies' counterpart to ResolveGroupWeightMultiplier - same Elite
        // Territory bias, keyed off the single enemy's own Tier instead of a group's membership.
        public static FP ResolveEnemyWeightMultiplier(Frame f, EnemyDataAsset data)
        {
            if (data == null)
                return FP._1;

            return ResolveWeightMultiplier(f, data.Tier >= EnemyTier.Elite);
        }

        private static FP ResolveWeightMultiplier(Frame f, bool isMajor)
        {
            FP multiplier = f.Global->EliteGroupWeightMultiplier;

            if (multiplier <= FP._0)
                return FP._1;

            return isMajor ? multiplier : FP._1;
        }

        // Team-wide Rift Shard gain, applied by RiftShardUtility.GrantAll BEFORE each player's own
        // CharacterStats.RiftShardGainMultiplier - so a run-wide bonus and a personal one compose
        // multiplicatively instead of one silently replacing the other.
        public static FP ResolveRiftShardGainMultiplier(Frame f)
        {
            return FPMath.Max(FP._0, FP._1 + f.Global->RiftShardGainBonus);
        }
    }
}
