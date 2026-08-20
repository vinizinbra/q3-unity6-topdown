namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;

    public static partial class DeterministicCommandSetup
    {
        static partial void AddCommandFactoriesUser(ICollection<IDeterministicCommandFactory> factories, RuntimeConfig gameConfig, SimulationConfig simulationConfig)
        {
            // A DeterministicCommand can register itself as its own IDeterministicCommandFactory
            // (see DeterministicCommand.GetCommandInstance) - no separate factory class needed.
            factories.Add(new GrantSkillUpgradeCommand());
            factories.Add(new RemoveSkillUpgradeCommand());
            factories.Add(new ClearSkillUpgradesCommand());
            factories.Add(new GrantWeaponPerkCommand());
            factories.Add(new GrantPassiveUpgradeCommand());
            factories.Add(new GrantGlobalUpgradeCommand());
            factories.Add(new GrantRiftMutationCommand());
            factories.Add(new SelectLevelUpUpgradeCommand());
            factories.Add(new RerollLevelUpOptionsCommand());
            factories.Add(new KeepCurrentWeaponCommand());
            factories.Add(new SelectSacrificeCommand());
            factories.Add(new CancelCursedRiftCommand());
            factories.Add(new SelectMutationCommand());
            factories.Add(new SkipBreathingCommand());
            factories.Add(new BuyStoreWeaponCommand());
            factories.Add(new BuyStoreFoodCommand());
            factories.Add(new BuyStoreWeaponLevelCommand());
            factories.Add(new CloseStoreCommand());
            factories.Add(new SelectBlacksmithPerkCommand());
            factories.Add(new CancelBlacksmithCommand());
            factories.Add(new SelfReviveCommand());
        }
    }
}