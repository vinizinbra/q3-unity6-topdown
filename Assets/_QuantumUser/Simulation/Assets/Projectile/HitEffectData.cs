namespace Quantum
{
    using Photon.Deterministic;

    // What happens to one target a hit resolved onto - composed as a list on ProjectileHitData (and
    // AreaDamage), so "damage, knock back, and spawn a shard" needs no new C#. The hit
    // decides who is caught; effects decide what happens to each of them.
    public abstract unsafe class HitEffectData : AssetObject
    {
        public abstract void Apply(Frame f, ref HitEffectContext context);
    }

    public struct HitEffectContext
    {
        public EntityRef Owner;

        // None when the projectile struck level geometry rather than an entity.
        public EntityRef Target;

        public FPVector3 Position;

        // Travel direction for a direct hit, radial from the blast center for an area hit - the
        // hit picks it, since only it knows which one makes sense.
        public FPVector3 PushDirection;

        public FP Damage;

        // Inherited by anything an effect spawns, so a grenade's lingering fire stays Weapon damage
        // and a dash's stays Skill damage.
        public DamageSource Source;

        // Neutral unless this hit traces back to a WeaponDataAsset.Element - StatusEffectUtility.
        // TryApplyElementalStatus reads this (via HitEffectUtility.ApplyToTarget) to deterministically
        // apply the matching status on a Weapon-sourced hit. Skill/enemy-attack sources leave this
        // Neutral, since they have no Element concept.
        public ElementType Element;

        // True for a genuine area/explosive blast (currently only AreaHitData.Detonate - Pixie's own
        // bomb - opts in) - read by DamageEffectData.Apply and passed through to
        // DamageUtility.ApplyDamage's own isExplosion parameter, which Pixie's Chain Reaction passive
        // gates its Instability marking on (see MarkExplosiveDeath.RequiresExplosion). False for a
        // plain single-target hit (a bullet, a melee swing) - never set explicitly at most call
        // sites, so it defaults false there for free.
        public bool IsExplosion;

        // Target's Rift Mark stack count as of the moment this hit started processing, captured by
        // HitEffectUtility.ApplyToTarget/WeaponSystem.FireHitscan BEFORE anything about this hit runs
        // (including this same hit's own Effects list). Every Rift Mark reaction-consumption check
        // (StatusEffectUtility.TryConsumeRiftMarkReaction, called from TryApplyElementalStatus and
        // from BurnEffectData/SlowEffectData's own guaranteed-element hooks) reads THIS instead of a
        // live re-read, so a mark this same hit applies (via RiftMarkEffectData, elsewhere in the
        // Effects list) can never be the one it consumes - see docs/elemental-reactions.md.
        public byte PreHitRiftMarkStacks;

        // Carried from Projectile.PelletIndex - see that field's own comment for why this exists
        // (Quantum's per-tick event dedup swallowing a multi-pellet weapon's overlapping hits).
        // 0 for anything that isn't a fanned pellet (a single-shot weapon, a skill, an AoE tick).
        public byte HitIndex;
    }
}
