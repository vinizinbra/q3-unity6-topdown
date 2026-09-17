namespace Quantum
{
    using Photon.Deterministic;

    // What happens to one target a hit resolved onto - composed as a list on ProjectileHitData (and
    // AreaDamage), so "damage, knock back, and spawn a shard" needs no new C#. The hit
    // decides who is caught; effects decide what happens to each of them.
    public abstract unsafe class HitEffectData : AssetObject
    {
        // False (the default, every existing effect - damage, knockback, a status) means "run once
        // PER TARGET a multi-target overlap query caught" - HitEffectUtility.ApplyToTarget's normal
        // shape. True (SpawnEntityEffectData only, today) means this effect describes something that
        // exists once per BLAST regardless of how many targets it also happened to catch - a
        // lingering hazard spawned at the blast's own center, not once per target at each target's
        // own position. Without this, a blast that caught 3 enemies would spawn 3 stacked copies of
        // the hazard (each independently ticking its own damage) at 3 different, essentially random
        // positions - see HitEffectUtility.ApplyBlastLevelEffects, which is what actually applies an
        // effect flagged true here, exactly once, whether the overlap query caught 0, 1, or many
        // targets. Irrelevant for a guaranteed single-connect hit (ApplyToTarget's multiTarget=false
        // path) - that already only ever calls Apply once regardless.
        public virtual bool AppliesOncePerBlast => false;

        public abstract void Apply(Frame f, ref HitEffectContext context);

        // Rank-2-scaled counterpart to Apply above - Zara's Remix ascension (see ZaraRemixUtility)
        // calls this instead of the plain 2-arg overload so its own "strengthened" rank 2 (+duration/
        // magnitude) reads generically off whichever concrete effect got randomly picked, rather than
        // Remix needing its own switch-on-type reimplementation of Burn/Slow/Stun. Default just
        // forwards to the plain Apply, ignoring both multipliers - every existing HitEffectData
        // subclass across every hero/weapon-perk/other-system caller is completely unaffected; only
        // the 3 concrete effects in Remix's own pool (BurnEffectData/SlowEffectData/StunEffectData)
        // override this.
        public virtual void Apply(Frame f, ref HitEffectContext context, FP durationMultiplier, FP magnitudeMultiplier)
            => Apply(f, ref context);
    }

    public struct HitEffectContext
    {
        public EntityRef Owner;

        // None when the projectile struck level geometry rather than an entity.
        public EntityRef Target;

        // The ENTITY that produced this hit - a spawned area/deployable (Zara's Totem, a fire trail),
        // as opposed to Owner, which is the player credited with it. None for a hit with no
        // persistent entity behind it (a hitscan shot, an already-consumed projectile's blast), which
        // is every caller that doesn't explicitly pass one.
        //
        // Exists so a per-deployable-instance effect (AreaAllyBudget's healing/cooldown caps - see
        // AreaAllyBudgetUtility) can find the specific instance that's paying, rather than having to
        // key off Owner (which would merge two of the same Zara's Totems into one shared budget) or
        // off the effect asset (shared by every instance). Set by AreaDamageSystem/
        // AlternatingAreaSystem.FireBonusPulse.
        public EntityRef SourceEntity;

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

        // The EXTRA element an Element Infusion weapon perk grafted on (Projectile.PerkElement),
        // applied alongside Element above via StatusEffectUtility.TryApplyInfusedElement rolling
        // PerkElementChance. Neutral/0 for anything without the perk, so it no-ops for free.
        public ElementType PerkElement;
        public FP PerkElementChance;

        // True for a genuine area/explosive blast (currently only AreaHitData.Detonate - Pixie's own
        // bomb - opts in) - read by DamageEffectData.Apply and passed through to
        // DamageUtility.ApplyDamage's own isExplosion parameter, which Pixie's Chain Reaction passive
        // gates its Instability marking on (see MarkExplosiveDeath.RequiresExplosion). False for a
        // plain single-target hit (a bullet, a melee swing) - never set explicitly at most call
        // sites, so it defaults false there for free.
        public bool IsExplosion;

        // Carried from Projectile.ForceCritical (Burst Rifle's 3rd-shot-always-crits) - passed
        // through to DamageUtility.ApplyDamage's own forceCritical param by DamageEffectData.Apply.
        // False for anything that isn't a forced-crit shot, never set explicitly at most call sites.
        public bool ForceCritical;

        // Carried from Projectile.PelletIndex - see that field's own comment for why this exists
        // (Quantum's per-tick event dedup swallowing a multi-pellet weapon's overlapping hits).
        // 0 for anything that isn't a fanned pellet (a single-shot weapon, a skill, an AoE tick).
        public byte HitIndex;

        // The spatial extent of the hit that produced this context, for any effect that wants to
        // care WHERE within a blast a target was caught - see SkillFocusUtility (Focused Power's
        // damage-toward-the-center falloff).
        //
        // Set only by the radius/shape/collider overlap paths in HitEffectUtility. AreaRadius stays
        // 0 for every direct hit and every single-target skill, and 0 is the explicit "this hit has
        // no meaningful area" reading - which is what lets a distance-from-center effect degrade to
        // a plain no-op for those instead of needing to know which skills are areas.
        public FPVector3 AreaCenter;
        public FP AreaRadius;
    }
}
