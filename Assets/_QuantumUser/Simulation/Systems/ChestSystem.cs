namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Collects a Chest once any player walks within its own authored PickupRadius - same
    // walk-into-radius auto-collect idiom as ExpOrb/ScrapOrb/RiftShardOrb/CoinOrb (see those
    // systems' own comments) rather than a new interact button - this project has no
    // interact-button/prompt pattern at all today. Unlike an orb, a Chest doesn't scale its radius
    // by CharacterStats.PickupRangeMultiplier - it's a deliberate walk-up-to-open prop, not a
    // passive magnet-scaled collectible.
    //
    // Unlike an orb, a Chest doesn't f.Destroy itself the same tick it's collected - it opens the
    // (possibly party-pausing) upgrade screen first via LevelUpUtility.BeginChestScreen, then adds
    // the existing generic DestroyAfterTime (OpenLingerDuration) instead of destroying outright, so
    // ChestView (View/Entities/Chest/ChestView.cs) has time to play its punch/shake/sprite-swap
    // reaction before the entity disappears. DestroyAfterTimeSystem lives INSIDE GameplaySystemGroup,
    // so that countdown doesn't even start ticking until gameplay resumes - the opened chest visibly
    // lingers for the whole screen, then cleans up shortly after. ChestSystem itself lives OUTSIDE
    // GameplaySystemGroup (see SystemSetup.User.cs) - unlike ExpOrbSystem (the SOLE trigger of
    // Global.LevelUpScreenOpen, safely paused inside the group), ChestSystem is a second, independent
    // trigger of that same flag and must keep ticking regardless of pause state to guard against a
    // double-open. See docs/chests.md.
    [Preserve]
    public unsafe class ChestSystem : SystemMainThreadFilter<ChestSystem.Filter>
    {
        // Comfortably longer than ChestView's own punch/shake + sprite-swap sequence - see that
        // class. Rarely matters in practice since DestroyAfterTimeSystem is paused for the whole
        // screen anyway (see class comment above), this only bounds the no-screen-rolled case.
        private static readonly FP OpenLingerDuration = 1;

        public override void Update(Frame f, ref Filter filter)
        {
            if (filter.Chest->Opened == true)
                return; // already opened - DestroyAfterTime (added below) cleans it up

            if (f.Global->LevelUpScreenOpen == true)
                return; // another screen (this Chest's own, or a real level-up) is already up

            var hits = EnemyMovementUtility.FindPlayersInRadius(f, filter.Transform3D->Position, filter.Chest->PickupRadius);

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef player = hits[i].Entity;

                if (f.Unsafe.TryGetPointer<Transform3D>(player, out var playerTransform) == false)
                    continue;

                FP sqrDistance = (playerTransform->Position - filter.Transform3D->Position).SqrMagnitude;

                if (sqrDistance > filter.Chest->PickupRadius * filter.Chest->PickupRadius)
                    continue;

                filter.Chest->Opened = true;
                f.AddOrGet<DestroyAfterTime>(filter.Entity, out var destroy);
                destroy->RemainingTime = OpenLingerDuration;
                LevelUpUtility.BeginChestScreen(f, player, filter.Chest->Kind);
                f.Events.ChestOpened(filter.Entity, player, filter.Transform3D->Position, filter.Chest->Kind);
                return;
            }
        }

        public struct Filter
        {
            public EntityRef Entity;
            public Chest* Chest;
            public Transform3D* Transform3D;
        }
    }
}
