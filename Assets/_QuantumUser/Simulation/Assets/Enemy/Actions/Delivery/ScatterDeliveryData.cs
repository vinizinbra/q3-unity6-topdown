namespace Quantum
{
    using Photon.Deterministic;

    // Resolves Count independent points scattered around the locked anchor and runs action.Effects
    // at each - this only chooses WHERE, same "delivery decides who/where, HitEffectData decides
    // what" split every other delivery already follows (MeleeAreaDeliveryData picks a target and
    // hands off to action.Effects; this picks a point instead). What actually happens at each point
    // is entirely up to the authored Effects list - a SpawnEntityEffectData (see that class, also
    // used by projectile impacts) drops an enemy/bomb prototype there, a DamageEffectData would
    // instead just hurt whatever's standing there, etc. Always instant (Begin() returns true) -
    // whatever an effect spawns is its own independent thing from here on, not tracked by this
    // delivery or by EnemySystem.
    //
    // action.Origin (same field GroundAreaDeliveryData reads - see its own comment) picks whether
    // the anchor is the locked target position (points scattered around whatever this enemy
    // targeted, a "you're being swarmed" read) or this enemy's own position (points scattered around
    // itself instead, e.g. reinforcements rallying next to their summoner).
    //
    // MinRandomOffset/MaxRandomOffset (inherited from EnemyDeliveryData, 0 by default there) should
    // both be authored non-zero here specifically - unlike most deliveries, which want their anchor
    // exact, every point landing exactly on the anchor defeats the point of scattering them.
    public unsafe class ScatterDeliveryData : EnemyDeliveryData
    {
        // How many independent points to resolve in one Begin() - each rolls its own random offset
        // around the anchor (not the same point repeated), so e.g. a summoner's minions or a
        // bomber's mines land scattered around it instead of stacked in an identical spot. Scaled
        // by live player count (f.PlayerCount) rather than a single flat value - same clamped-
        // [1,4]-then-switch idiom BalanceConfig.GetCoopGlobal/GetCoopHp already use, just living on
        // this asset directly since point COUNT is an attack-shape parameter, not a global economy
        // multiplier those route through BalanceConfig for. All 4 default equal (1) so an
        // un-retuned asset scatters the same regardless of party size, same as the old flat Count
        // field this replaces.
        public int CountP1 = 1;
        public int CountP2 = 1;
        public int CountP3 = 1;
        public int CountP4 = 1;

        private int ResolveCount(Frame f)
        {
            int clamped = f.PlayerCount < 1 ? 1 : (f.PlayerCount > 4 ? 4 : f.PlayerCount);
            return clamped switch { 1 => CountP1, 2 => CountP2, 3 => CountP3, _ => CountP4 };
        }

        public override bool Begin(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            FPVector3 anchor = action.Origin == EnemyActionOrigin.Self ? filter.Transform3D->Position : filter.Enemy->SkillTargetPosition;
            int count = ResolveCount(f);

            for (int i = 0; i < count; i++)
            {
                FPVector3 point = RandomizeAroundAnchor(f, anchor);

                // Target is deliberately EntityRef.None - a scattered point has nothing standing on
                // it yet by definition (see HitEffectContext.Target's own comment on this being an
                // already-established valid state, same as a projectile striking level geometry).
                HitEffectContext context = new HitEffectContext
                {
                    Owner = filter.Entity,
                    Target = EntityRef.None,
                    Position = point,
                    PushDirection = default,
                    Damage = action.Damage,
                    Source = DamageSource.None,
                    Element = ElementType.Neutral,
                };

                HitEffectUtility.ApplyToTarget(f, action.Effects, ref context);
            }

            return true;
        }
    }
}
