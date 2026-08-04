namespace Quantum
{
    using Photon.Deterministic;

    // Global balance tuning for the ExplodeOnDeath mechanic - one shared knob for "how hard does a
    // marked enemy's death explosion hit", instead of a DamagePercent authored separately on each
    // upgrade that grants the mark (Max's Berserk upgrade, Pixie's bomb upgrade, any future one).
    // Referenced via RuntimeConfig.ExplodeOnDeathConfig.
    public class ExplodeOnDeathConfig : AssetObject
    {
        // Percent of the dying enemy's own MaxHealth - tougher enemies explode harder with zero
        // extra tuning per enemy type, same reasoning DamageUtility already applies to Radius
        // scaling off the enemy's real collider radius (EnemyMovementUtility.ResolveEntityRadius).
        public FP DamagePercent = FP._0_10;

        // Multiplies the dying enemy's own real collider radius rather than a flat radius, so
        // a Brute's explosion still scales bigger than a Grunt's the same way the base radius does -
        // this just tunes the ratio between "how big I am" and "how big I explode" globally.
        public FP RadiusMultiplier = FP._1;

        // Enemies by default - a chain of marked kills going off next to a downed ally shouldn't
        // also hurt them. Change to Both if friendly fire from the chain is ever actually wanted.
        public DamageTargetMask TargetMask = DamageTargetMask.Enemies;

        // How long a fresh ExplodeOnDeath mark lasts before ExplodeOnDeathTimerSystem removes it
        // unfulfilled - see TryMarkExplodeOnDeath, which refreshes this back to full on every
        // additional marked hit. One shared knob, same reasoning as DamagePercent/RadiusMultiplier
        // above.
        public FP Duration = 5;

        // A target that's currently Rift-Marked (StatusEffectUtility.IsRiftMarked - see
        // docs/elemental-reactions.md) detonates bigger and harder when its own ExplodeOnDeath mark
        // goes off, regardless of which system granted that mark (Pixie's Chain Reaction or Max's
        // Berserk). Independently tunable from RadiusMultiplier/DamagePercent since this is a
        // separate bonus, not a replacement.
        public FP RiftMarkRadiusMultiplier = FP._2;
        public FP RiftMarkDamageMultiplier = FP._2;
    }
}
