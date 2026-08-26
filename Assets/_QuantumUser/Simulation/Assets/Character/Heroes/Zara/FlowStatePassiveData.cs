namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Zara's base Passive - Flow State. Adds the persistent ZaraFlow component onto her own entity
    // (same "spawn-time bake adds a component" shape SeedShield/SeedArmor already use in
    // CharacterSystem, and the same "field lives on the hero" shape Kai's ProjectileSlowField and
    // Brute's ProtectorAura use). Passive Ascensions mutate that component directly rather than
    // CharacterStats, since none of these tunables are generic hero stats.
    //
    // Replaced ResonancePassiveData outright. Resonance was a damage-fed meter that fired an automatic
    // pulse; Flow is a movement-fed STATE that continuously buffs her, rewarding constant intentional
    // movement and aggressive Dash use and punishing standing still or getting hit.
    //
    // Two things only: a bar that fills, and whether it is on. It began as a 3-stack ladder; that was
    // more machinery than the fantasy needed, since "am I in the groove" is a binary a player reads
    // instantly while "am I on stack 2 or 3" is bookkeeping.
    //
    // Flow belongs to ZARA, never to her Totem - the only deliberate crossover between the two in this
    // design is Headliner rank 2 (see HeadlinerPassiveUpgradeData).
    public unsafe partial class FlowStatePassiveData : PassiveData
    {
        [Header("Flow")]
        [Tooltip("Seconds of continuous meaningful movement to fill the bar from empty. Faster Tempo divides this by its own rate multiplier rather than overwriting it.")]
        public FP BuildDuration = FP.FromString("2.5");

        [Tooltip("Move Speed while Flow is ACTIVE (0.15 = +15%). Flat - Flow is on or off, there is no ladder.")]
        public FP MoveSpeedBonus = FP.FromString("0.15");

        [Tooltip("Fire Rate while Flow is ACTIVE (0.15 = +15%).")]
        public FP FireRateBonus = FP.FromString("0.15");

        [Header("Movement")]
        [Tooltip("Minimum movement-INPUT magnitude that counts as intentional. Read off the player's own input rather than velocity, which is what makes knockback, teleports and physics shoves unable to build Flow. Exists to reject analog-stick noise on a resting thumb.")]
        public FP MovementInputThreshold = FP._0_10;

        [Header("Stationary")]
        [Tooltip("How long she may stand still before the bar starts draining. Below this it is simply held - a brief stop to fire or turn must never cost rhythm.")]
        public FP StationaryGrace = FP.FromString("1.25");

        [Tooltip("Seconds for a FULL bar to drain away while she stays still. A duration rather than a rate, because 'it empties in 4.5s' is what actually gets tuned.")]
        public FP DecayDuration = FP.FromString("4.5");

        public override void Apply(Frame f, EntityRef entity, CharacterStats* stats)
        {
            f.Add(entity, new ZaraFlow
            {
                Progress = FP._0,
                IsActive = false,
                IsMoving = false,
                StationaryTimer = FP._0,

                BuildDuration = BuildDuration,
                MoveSpeedBonus = MoveSpeedBonus,
                FireRateBonus = FireRateBonus,
                MovementInputThreshold = MovementInputThreshold,
                StationaryGrace = StationaryGrace,
                DecayDuration = DecayDuration,

                // Captured once, here, so every later toggle rebakes the bonus from a clean baseline
                // instead of compounding off an already-buffed value. CharacterStats has already been
                // seeded from CharacterData by the time a Passive is applied (see CharacterSystem's own
                // ordering), so these are her true unbuffed multipliers.
                BaseMoveSpeedMultiplier = stats->MoveSpeedMultiplier,
                BaseAttackSpeedMultiplier = stats->AttackSpeedMultiplier,

                // Every Ascension-owned field starts off - Faster Tempo / Second Wind / Headliner each
                // turn on their own half. Written explicitly rather than left to zero-init so the full
                // contract is visible in one place.
                BuildRateMultiplier = FP._1,
                ActiveFireRateBonus = FP._0,

                SecondWindMoveSpeedBonus = FP._0,
                SecondWindDuration = FP._0,
                ProgressRetainedOnHit = FP._0,
                KeepTheBeatDamageReduction = FP._0,
                KeepTheBeatCooldown = FP._0,
                KeepTheBeatCooldownRemaining = FP._0,

                ActiveDamageBonus = FP._0,
                ActiveBeatEffectiveness = FP._0,
                HypeRadius = FP._0,
                HypeDuration = FP._0,
                HypeMoveSpeedBonus = FP._0,
                HypeFireRateBonus = FP._0,
                HypeCooldown = FP._0,
                HypeCooldownRemaining = FP._0,
            });
        }
    }
}
