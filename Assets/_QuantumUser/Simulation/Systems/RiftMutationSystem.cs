namespace Quantum
{
    using UnityEngine.Scripting;

    // Per-tick command processor for LevelUpPoolKind.RiftMutation - mirrors GlobalUpgradeSystem
    // exactly, just for the Rift Mutation pool. Currently only reached by the debug grant button
    // (see RiftMutationDebugTrigger) - a real level-up screen already grants through
    // LevelUpUtility.GrantOption -> RiftMutationUtility.Grant directly, with no command involved at
    // all (see docs/rift-mutations.md).
    [Preserve]
    public unsafe class RiftMutationSystem : SystemMainThreadFilter<RiftMutationSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            if (f.GetPlayerCommand(filter.PlayerLink->Player) is not GrantRiftMutationCommand command)
                return;

            RiftMutationUtility.Grant(f, filter.Entity, command.Mutation);
            Log.Debug($"[RiftMutation] {filter.Entity} was granted Rift Mutation {command.Mutation} via command");
        }

        public struct Filter
        {
            public EntityRef Entity;
            public PlayerLink* PlayerLink;
        }
    }
}
