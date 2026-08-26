namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Ticks every actively-holding reviver's own ReviveChannel (see docs/revive.md) - validity,
    // progress accumulation and completion. Taking damage is handled entirely by
    // ReviveDamageInterruptSystem (a signal-driven Cancel, not something this system polls for) -
    // by the time this runs again after a hit, the ReviveChannel is simply gone. Always a TEAMMATE
    // revive of a DOWNED player - KO has no revive path at all anymore, teammate or self (see
    // PlayerLifeStateUtility.EnterKO) - and self-revive is a separate, unrelated instant path
    // (ReviveUtility.TryPerformSelfRevive via SelfReviveCommand) that never creates a ReviveChannel
    // at all. Filters on the REVIVER, not the target; reads Input.HeroSkill.IsDown directly off this
    // tick's raw input rather than through SkillSystem's own local-variable neutralization, which
    // never mutates the underlying Input struct - so this is unaffected by SkillSystem's own
    // ordering/neutralization either way.
    [Preserve]
    public unsafe class ReviveChannelSystem : SystemMainThreadFilter<ReviveChannelSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            EntityRef reviver = filter.Entity;
            ReviveChannel* channel = filter.ReviveChannel;
            EntityRef target = channel->Target;

            // Target reaching KO shouldn't actually be reachable mid-channel (the bleed-out timer
            // that drives Downed -> KO is itself paused the instant ReviveHolder != None - see
            // PlayerLifeStateSystem), but this is re-validated the same "never trust it" way every
            // other invalidation reason here already is.
            if (f.Exists(target) == false || f.Unsafe.TryGetPointer<PlayerLifeState>(target, out var targetLifeState) == false
                || targetLifeState->State != PlayerLifeStateKind.Downed)
            {
                ReviveUtility.Cancel(f, reviver);
                return;
            }

            if (PlayerLifeStateUtility.IsIncapacitated(f, reviver) == true)
            {
                ReviveUtility.Cancel(f, reviver);
                return;
            }

            ReviveConfig config = PlayerLifeStateUtility.GetConfig(f);
            FP range = config != null ? config.ReviveInteractionRange : 3;

            if (f.Unsafe.TryGetPointer<Transform3D>(target, out var targetTransform) == true)
            {
                FP sqrDistance = EnemyMovementUtility.FlatSqrDistance(filter.Transform3D->Position, targetTransform->Position);

                if (sqrDistance > range * range)
                {
                    ReviveUtility.Cancel(f, reviver);
                    return;
                }
            }

            var input = PlayerInputUtility.Resolve(f, filter.Entity, filter.PlayerLink);

            if (input->HeroSkill.IsDown == false)
            {
                ReviveUtility.Cancel(f, reviver);
                return;
            }

            FP duration = config != null ? config.DownedReviveDuration : (FP._2 + FP._0_50);

            targetLifeState->ReviveProgress += f.DeltaTime;

            if (targetLifeState->ReviveProgress < duration)
                return;

            PlayerLifeStateUtility.Revive(f, target, reviver);
            f.Remove<ReviveChannel>(reviver);
        }

        public struct Filter
        {
            public EntityRef Entity;
            public ReviveChannel* ReviveChannel;
            public PlayerLink* PlayerLink;
            public Transform3D* Transform3D;
        }
    }
}
