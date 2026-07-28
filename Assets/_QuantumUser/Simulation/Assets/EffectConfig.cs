namespace Quantum
{
    using Photon.Deterministic;

    // Magnitude bucket for KnockbackEffectData - reused across every weapon/skill/enemy attack that
    // pushes, instead of each authoring its own Force/UpwardForce pair (see EffectConfig.GetKnockback).
    // Direction is never part of this - that still comes from the hit itself
    // (HitEffectContext.PushDirection), only "how hard" is centralized.
    public enum KnockbackTier
    {
        Small,
        Medium,
        Strong,
    }

    // Global balance tuning for every status effect - one shared knob per status instead of authored
    // separately on every {Burn,Poison,Stun,Slow,Mark,Haste}EffectData asset and duplicated again as
    // private constants in StatusEffectUtility.TryApplyElementalStatus's elemental-proc path.
    // Referenced via RuntimeConfig.EffectConfig, read by StatusEffectUtility and the EffectData
    // classes above.
    //
    // Burn/Poison are DoT: total damage dealt over Duration is HitDamage * DamagePercent, spread
    // evenly across TickInterval-spaced ticks - see StatusEffectUtility.ComputeDotDamagePerTick.
    // Everything else just needs a Duration, plus a magnitude for Slow/Mark/Haste.
    public class EffectConfig : AssetObject
    {
        // Shared DoT cadence for Burn and every Poison stack - not stored per-instance, since nothing
        // here needs a different tick rate per proc. A status applied fresh ticks for the first time
        // TickInterval seconds later, not immediately. See StatusEffectUtility.ApplyBurn/ApplyPoison
        // (timer seeding) and ComputeDotDamagePerTick (ticks = Duration / TickInterval).
        public FP TickInterval = FP._0_50;

        public FP BurnDuration = 3;
        public FP BurnDamagePercent = FP._0_10;

        // Minimum total Burn damage over BurnDuration, as a percent of the OWNER's own MaxHealth,
        // spread across ticks the same way BurnDamagePercent is - whichever of the two (hit-based or
        // this floor) is bigger wins. Covers a hit that dealt 0 direct damage (a knockback-only proc,
        // a heal pulse) as well as a real but small hit whose BurnDamagePercent share would otherwise
        // be negligible. See StatusEffectUtility.ComputeDotDamagePerTickWithFloor.
        public FP BurnFloorPercent = FP._0_05;

        public FP PoisonDuration = 3;

        // Kept low relative to BurnDamagePercent on purpose - Poison stacks up to 5 independent
        // slots (see StatusEffects.qtn/StatusEffectUtility.ApplyPoison), so its real ceiling is
        // PoisonDamagePercent * 5, not this number alone. At 2%, max-stacked Poison (10%) just
        // reaches single-instance Burn (10%); anything short of 5 simultaneous stacks - the common
        // case - stays weaker than Burn.
        public FP PoisonDamagePercent = FP.FromString("0.02");

        // Minimum total Poison damage over PoisonDuration per stack, same "whichever is bigger wins"
        // rule as BurnFloorPercent - half of PoisonDamagePercent, same ratio as Burn's own
        // Floor:DamagePercent (5%:10%).
        public FP PoisonFloorPercent = FP._0_01;

        public FP StunDuration = 1;

        // Root - currently only granted by JuggernautLandingRootSkillAction, baked through
        // JuggernautLandingRootUpgrade/JuggernautLaunched at launch time; see that class for why
        // Duration alone moves here while RootChance/Damage (skill-specific, not effect-generic)
        // stay authored on the skill itself.
        public FP RootDuration = 2;

        public FP SlowDuration = 3;
        public FP SlowSpeedMultiplier = FP._0_50;

        public FP MarkDuration = 5;
        public FP MarkDamageTakenMultiplier = FP.FromString("1.2");

        // Haste (buff) - also reused by SentryAuraSystem's Fire Rate aura as its lingering refresh
        // window, so "how long Haste lingers" stays tuned in one place regardless of source.
        public FP HasteDuration = 5;
        public FP HasteAttackSpeedMultiplier = FP.FromString("1.5");

        // Knockback - one Force/UpwardForce pair per KnockbackTier, reused by every
        // KnockbackEffectData in the game (see KnockbackEffectData.Tier) instead of each authoring
        // its own pair. X: horizontal push. Y: vertical pop - ground friction is ~20x air friction,
        // so Force alone gets eaten while grounded unless UpwardForce briefly launches the target
        // airborne (see DamageUtility.ApplyKnockback) - kept proportionally smaller than Force at
        // every tier so Strong doesn't launch targets absurdly high just to also shove them far.
        public FP SmallKnockbackForce = 4;
        public FP SmallKnockbackUpwardForce = 2;

        public FP MediumKnockbackForce = 8;
        public FP MediumKnockbackUpwardForce = 4;

        public FP StrongKnockbackForce = 16;
        public FP StrongKnockbackUpwardForce = 6;

        public void GetKnockback(KnockbackTier tier, out FP force, out FP upwardForce)
        {
            switch (tier)
            {
                case KnockbackTier.Small:
                    force = SmallKnockbackForce;
                    upwardForce = SmallKnockbackUpwardForce;
                    break;

                case KnockbackTier.Strong:
                    force = StrongKnockbackForce;
                    upwardForce = StrongKnockbackUpwardForce;
                    break;

                default:
                    force = MediumKnockbackForce;
                    upwardForce = MediumKnockbackUpwardForce;
                    break;
            }
        }
    }
}
