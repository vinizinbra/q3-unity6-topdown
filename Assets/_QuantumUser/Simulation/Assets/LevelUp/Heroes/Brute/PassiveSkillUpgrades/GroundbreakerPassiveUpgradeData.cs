namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Ranked Passive Ascension (Groundbreaker) - Brute's third Protector-pool line, replacing
    // Unstoppable. Terrain aggression: he weaponizes his own weight by dropping onto enemies from
    // higher ground. See Groundbreaker.qtn / BruteGroundbreakerSystem.
    //
    //  - Rank 1 "Heavy Landing": landing from a real drop throws nearby enemies away.
    //  - Rank 2 "Crash Landing": harder knockback, real impact damage, and anything slammed into a
    //    wall is Stunned.
    //  - Rank 3 "Seismic Impact": bigger radius, harder still, and anything actually WALL-STUNNED is
    //    left Exposed - a burst window for the whole team.
    //
    // It deliberately shares no design space with Momentum: nothing here generates, retains, resets or
    // spends Momentum, grants Move Speed, or changes Juggernaut's duration. Each rank SETS the total
    // values (not additive across ranks), same convention every other ranked Ascension uses.
    public unsafe partial class GroundbreakerPassiveUpgradeData : PassiveUpgradeData
    {
        [Header("Landing trigger")]
        [Tooltip("How far Brute must DROP (world units) for a landing to count. The level has no discrete height-level grid, so this is a plain vertical distance. 2 is deliberately double MovementDataAsset.MaxLedgeHeight (1, the tallest ledge he can auto-mantle), so ordinary traversal - steps, slopes, same-height dashes, the known false auto-hop at chunk seams - can never reach it.")]
        public FP MinimumFallHeight = 2;

        [Tooltip("Which kinds of airtime qualify. All three by default - the height requirement above is the real filter, and the brief wants this working from terrain drops, platforms, jumps and future launch mechanics alike.")]
        public bool AllowFallLandings = true;
        public bool AllowJumpLandings = true;
        public bool AllowLaunchedLandings = true;

        [Header("Impact shockwave")]
        public FP[] ImpactRadius = { FP._3, FP._3, FP.FromString("4.5") };

        [Tooltip("Per rank. Rank 3 is roughly +65% over rank 1.")]
        public FP[] KnockbackForce = { 10, 14, FP.FromString("16.5") };
        public FP KnockbackUpwardForce = FP._2;

        [Tooltip("Fraction of Juggernaut Skill Damage, resolved live at landing time. Rank 1 is deliberately low - the fantasy there is scattering a group, not killing it.")]
        public FP[] ImpactDamagePercent = { FP.FromString("0.20"), FP._0_50, FP.FromString("0.75") };

        [Tooltip("Highest EnemyTier affected. Boss (all tiers) by default - per-tier knockback resistance already scales the shove down on its own, so a hard skip on top would double-punish.")]
        public EnemyTier MaxAffectedTier = EnemyTier.Boss;

        [Header("Rank 2 - wall slam")]
        public FP WallStunDuration = FP._1;

        [Tooltip("How far past a knocked-back enemy to probe for a wall. Matches Iron Shoulder's own value - both go through the same shared WallSlamUtility.")]
        public FP WallCheckDistance = FP._2;

        [Header("Rank 3 - Exposed")]
        [Tooltip("Extra damage taken. Applied ONLY to enemies this landing actually wall-STUNNED, never to everyone caught in the shockwave.")]
        public FP VulnerabilityDamageTakenModifier = FP._0_25;
        public FP VulnerabilityDuration = FP._3;

        public override void Apply(Frame f, EntityRef entity, int rank)
        {
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            f.AddOrGet<GroundbreakerUpgrade>(entity, out var upgrade);

            upgrade->MinimumFallHeight = MinimumFallHeight;
            upgrade->AllowedLandingSources = ResolveLandingSourceMask();

            upgrade->ImpactRadius = ImpactRadius[index];
            upgrade->KnockbackForce = KnockbackForce[index];
            upgrade->KnockbackUpwardForce = KnockbackUpwardForce;
            upgrade->ImpactDamagePercent = ImpactDamagePercent[index];
            upgrade->MaxAffectedTierIndex = (byte)MaxAffectedTier;

            // Rank 2+ - false at rank 1 is what leaves the whole wall reaction (and therefore rank 3's
            // Exposed window, which is gated behind a landed wall Stun) off entirely.
            upgrade->WallStunEnabled = rank >= 2;
            upgrade->WallStunDuration = WallStunDuration;
            upgrade->WallCheckDistance = WallCheckDistance;

            upgrade->VulnerabilityEnabled = rank >= 3;
            upgrade->VulnerabilityDamageTakenModifier = VulnerabilityDamageTakenModifier;
            upgrade->VulnerabilityDuration = VulnerabilityDuration;
        }

        // Bitmask over LandingSource (1 << (byte)source) - three authored bools rather than a raw
        // number in the Inspector, since a designer shouldn't have to know the enum's integer values.
        private byte ResolveLandingSourceMask()
        {
            int mask = 0;

            if (AllowFallLandings == true) mask |= 1 << (byte)LandingSource.Fall;
            if (AllowJumpLandings == true) mask |= 1 << (byte)LandingSource.Jump;
            if (AllowLaunchedLandings == true) mask |= 1 << (byte)LandingSource.Launched;

            return (byte)mask;
        }
    }
}
