namespace Quantum
{
    using UnityEngine.Scripting;

    // Per-tick command processor for LevelUpPoolKind.PassiveUpgrade - mirrors WeaponSystem's own
    // ProcessGrantPerkCommand/SkillSystem's own ProcessGrantUpgradeCommand, just for the one pool
    // kind neither of those already has a home for. Currently only reached by the debug grant button
    // (see PassiveUpgradeDebugTrigger) - a real level-up screen already grants through
    // LevelUpUtility.GrantOption -> PassiveUpgradeUtility.Grant directly, with no command involved at
    // all (see docs/level-up-upgrades.md).
    [Preserve]
    public unsafe class PassiveUpgradeSystem : SystemMainThreadFilter<PassiveUpgradeSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            if (f.GetPlayerCommand(filter.PlayerLink->Player) is not GrantPassiveUpgradeCommand command)
                return;

            PassiveUpgradeUtility.Grant(f, filter.Entity, command.Upgrade);
            LevelUpUtility.RecordHistory(f, filter.Entity, LevelUpPoolKind.PassiveUpgrade, new AssetRef<UpgradeData>(command.Upgrade.Id));
            Log.Debug($"[Passive] {filter.Entity} was granted Passive Upgrade {command.Upgrade} via command");
        }

        public struct Filter
        {
            public EntityRef Entity;
            public PlayerLink* PlayerLink;
        }
    }
}
