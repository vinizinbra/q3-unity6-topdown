namespace Quantum
{
    using UnityEngine.Scripting;

    // Per-tick command processor for LevelUpPoolKind.GlobalUpgrade - mirrors PassiveUpgradeSystem's
    // own ProcessGrantCommand, just for the one pool kind that didn't have a debug-grant path yet.
    // Currently only reached by the debug grant button (see GlobalUpgradeDebugTrigger) - a real
    // level-up screen already grants through LevelUpUtility.GrantOption -> GlobalUpgradeUtility.Grant
    // directly, with no command involved at all (see docs/level-up-upgrades.md).
    [Preserve]
    public unsafe class GlobalUpgradeSystem : SystemMainThreadFilter<GlobalUpgradeSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            if (f.GetPlayerCommand(filter.PlayerLink->Player) is not GrantGlobalUpgradeCommand command)
                return;

            GlobalUpgradeUtility.Grant(f, filter.Entity, command.Upgrade);
            LevelUpUtility.RecordHistory(f, filter.Entity, LevelUpPoolKind.GlobalUpgrade, new AssetRef<UpgradeData>(command.Upgrade.Id));
            Log.Debug($"[GlobalUpgrade] {filter.Entity} was granted Global Upgrade {command.Upgrade} via command");
        }

        public struct Filter
        {
            public EntityRef Entity;
            public PlayerLink* PlayerLink;
        }
    }
}
