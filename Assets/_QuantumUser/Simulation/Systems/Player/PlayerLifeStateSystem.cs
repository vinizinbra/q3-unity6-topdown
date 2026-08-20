namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Ticks a Downed player's own bleed-out timer (see docs/revive.md/PlayerLifeState.qtn) - the
    // ONLY path that drives Downed -> KO, since a Downed player is damage-immune (Invulnerable, see
    // PlayerLifeStateUtility.EnterDowned). Does NOT drive Alive -> Downed - that's synchronous,
    // triggered directly from DamageUtility.ApplyDamage's own lethal-damage branch. Also decays a
    // Downed/KO player's own banked ReviveProgress while nobody currently holds ReviveHolder (an
    // interrupted revive - ReviveDamageInterruptSystem/ReviveUtility.Cancel - no longer resets
    // progress to 0 outright, it just stops advancing and drifts back down over time instead), and
    // processes this player's own SelfReviveCommand (a deliberate single press/confirm, sent from a
    // dedicated View window - see docs/revive.md) - both folded in here rather than separate
    // systems, since this already iterates every player with PlayerLifeState each tick.
    [Preserve]
    public unsafe class PlayerLifeStateSystem : SystemMainThreadFilter<PlayerLifeStateSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            if (f.GetPlayerCommand(filter.PlayerLink->Player) is SelfReviveCommand)
            {
                ReviveUtility.TryPerformSelfRevive(f, filter.Entity);
            }

            PlayerLifeState* lifeState = filter.PlayerLifeState;

            if (lifeState->State == PlayerLifeStateKind.Alive)
                return;

            // Same "paused/held while actively channeling" guard the bleed-out timer below already
            // uses - never fights an in-progress hold's own accumulation.
            if (lifeState->ReviveHolder == EntityRef.None && lifeState->ReviveProgress > FP._0)
            {
                ReviveConfig config = PlayerLifeStateUtility.GetConfig(f);
                FP decayRate = config != null ? config.ReviveProgressDecayRate : FP._0_50;

                lifeState->ReviveProgress = FPMath.Max(FP._0, lifeState->ReviveProgress - decayRate * f.DeltaTime);
            }

            if (lifeState->State != PlayerLifeStateKind.Downed)
                return;

            // Paused the instant someone starts holding to revive this player - a near-complete
            // revive can never get yanked into KO by an unlucky timer expiry mid-channel.
            if (lifeState->ReviveHolder != EntityRef.None)
                return;

            lifeState->BleedOutRemaining -= f.DeltaTime;

            if (lifeState->BleedOutRemaining <= FP._0)
            {
                PlayerLifeStateUtility.EnterKO(f, filter.Entity);
            }
        }

        public struct Filter
        {
            public EntityRef Entity;
            public PlayerLink* PlayerLink;
            public PlayerLifeState* PlayerLifeState;
        }
    }
}
