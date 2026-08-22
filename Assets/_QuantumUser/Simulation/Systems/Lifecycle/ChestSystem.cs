namespace Quantum
{
    using System;
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
    // GameplaySystemGroup (see SystemSetup.User.cs) - unlike CurrencyOrbSystem (the SOLE trigger of
    // Global.LevelUpScreenOpen, safely paused inside the group), ChestSystem is a second, independent
    // trigger of that same flag and must keep ticking regardless of pause state to guard against a
    // double-open. See docs/chests.md.
    [Preserve]
    public unsafe class ChestSystem : SystemMainThreadFilter<ChestSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            if (filter.Chest->Opened == true)
                return; // already opened - DestroyAfterTime (added below) cleans it up

            if (f.Global->LevelUpScreenOpen == true)
                return; // another screen (this Chest's own, or a real level-up) is already up

            // Candidates come from PlayerQueryUtility, not a Player-layer physics query. Besides
            // dropping the per-chest per-tick frame-heap allocation, this fixes a real bug: the old
            // query returned everything on the Player layer and this loop only required a
            // Transform3D, so a Lux SENTRY (authored on layer 7, no PlayerLink) parked inside the
            // radius opened the chest and was handed to BeginChestScreen as the "player".
            Span<EntityRef> players = stackalloc EntityRef[PlayerQueryUtility.MaxPlayers];
            int playerCount = PlayerQueryUtility.GatherPlayers(f, players);

            EntityRef opener = EntityRef.None;
            FP closestSqrDistance = default;

            for (int i = 0; i < playerCount; i++)
            {
                EntityRef player = players[i];

                if (f.Unsafe.TryGetPointer<Transform3D>(player, out var playerTransform) == false)
                    continue;

                FP sqrDistance = (playerTransform->Position - filter.Transform3D->Position).SqrMagnitude;

                if (sqrDistance > filter.Chest->PickupRadius * filter.Chest->PickupRadius)
                    continue;

                // Nearest wins, rather than whoever came first in the old query's hit order.
                if (opener == EntityRef.None || sqrDistance < closestSqrDistance)
                {
                    opener = player;
                    closestSqrDistance = sqrDistance;
                }
            }

            if (opener != EntityRef.None)
            {
                filter.Chest->Opened = true;
                f.AddOrGet<DestroyAfterTime>(filter.Entity, out var destroy);
                // Linger exactly ONE rendered frame after the upgrade screen closes, then hide.
                // ChestView's punch/shake + sprite-swap + open particle all run on UNSCALED time, so
                // they've already played out during the paused screen. DestroyAfterTimeSystem is
                // paused for that whole screen (it lives inside GameplaySystemGroup) and only destroys
                // once RemainingTime is no longer > 0, so FP._0 / a single f.DeltaTime would tick to
                // <= 0 and destroy on the FIRST resumed frame - before the view ever draws it ("gone").
                // Two sim steps makes it survive that first frame (rendered once) and destroy on the
                // next - one visible frame of linger, frame-rate independent.
                destroy->RemainingTime = f.DeltaTime * 2;
                LevelUpUtility.BeginChestScreen(f, opener, filter.Chest->Kind);
                f.Events.ChestOpened(filter.Entity, opener, filter.Transform3D->Position, filter.Chest->Kind);
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
