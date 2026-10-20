namespace Quantum
{
    using System;
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // A Traversal Challenge's return pad (see TraversalChallenge.ReturnTeleportPrototype /
    // docs/traversal-challenge.md): any non-incapacitated player who STANDS inside Radius for
    // ChargeTime seconds is teleported to Destination (ground-snapped near the activator by
    // TraversalChallengeUtility.SpawnReturnTeleport). Leaving the radius resets that player's
    // charge. Same KCC.Teleport + zero-every-velocity reset RunPhaseUtility.
    // TeleportPlayersToBossArena/PlayerFallSystem use - KCC.Teleport alone keeps the player's
    // momentum. No re-trigger guard needed: Destination is never on a pad.
    // Inside GameplaySystemGroup, right after TraversalChallengeSystem - frozen with everything else
    // while an upgrade screen is open.
    [Preserve]
    public unsafe class TraversalReturnTeleportSystem : SystemMainThreadFilter<TraversalReturnTeleportSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            TraversalReturnTeleport* pad = filter.Teleport;
            FPVector3 center = filter.Transform3D->Position;
            FP radiusSqr = pad->Radius * pad->Radius;

            var chargers = pad->Charger;
            var charges = pad->Charge;
            Span<bool> seen = stackalloc bool[chargers.Length];

            var players = f.Filter<PlayerLink, Transform3D>();

            while (players.Next(out EntityRef player, out PlayerLink _, out Transform3D playerTransform))
            {
                if (PlayerLifeStateUtility.IsIncapacitated(f, player) == true)
                    continue;

                if ((playerTransform.Position - center).SqrMagnitude > radiusSqr)
                    continue;

                int slot = FindSlot(chargers, player);

                if (slot < 0)
                    slot = FindSlot(chargers, EntityRef.None);

                if (slot < 0)
                    continue;

                seen[slot] = true;
                chargers[slot] = player;
                charges[slot] += f.DeltaTime;

                if (charges[slot] < pad->ChargeTime)
                    continue;

                Teleport(f, filter.Entity, pad, player, playerTransform.Position);
                chargers[slot] = EntityRef.None;
                charges[slot] = FP._0;
            }

            // Anyone who stepped off (or teleported, or got downed) loses their charge.
            for (int i = 0; i < chargers.Length; i++)
            {
                if (seen[i] == true)
                    continue;

                chargers[i] = EntityRef.None;
                charges[i] = FP._0;
            }
        }

        private static void Teleport(Frame f, EntityRef pad, TraversalReturnTeleport* teleport, EntityRef player, FPVector3 from)
        {
            FPVector3 to = teleport->Destination;

            if (f.Unsafe.TryGetPointer<KCC>(player, out var kcc) == true)
            {
                kcc->Teleport(f, to);
                kcc->SetKinematicVelocity(FPVector3.Zero);
                kcc->SetDynamicVelocity(FPVector3.Zero);
                kcc->SetExternalImpulse(FPVector3.Zero);
            }
            else if (f.Unsafe.TryGetPointer<Transform3D>(player, out var transform) == true)
            {
                transform->Position = to;
            }

            f.Events.TraversalReturnTeleported(player, pad, from, to);

            Log.Debug($"[TraversalChallenge] {player} took return pad {pad} back to {to}");
        }

        private static int FindSlot(FixedArray<EntityRef> chargers, EntityRef entity)
        {
            for (int i = 0; i < chargers.Length; i++)
            {
                if (chargers[i] == entity)
                    return i;
            }

            return -1;
        }

        public struct Filter
        {
            public EntityRef Entity;
            public TraversalReturnTeleport* Teleport;
            public Transform3D* Transform3D;
        }
    }
}
