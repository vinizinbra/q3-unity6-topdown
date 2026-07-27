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
        }
    }
}