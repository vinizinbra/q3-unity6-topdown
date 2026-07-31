namespace Quantum
{
    using Photon.Deterministic;

    // Lets a client grant a Passive Ascension to its own player outside the normal level-up flow -
    // the same PassiveUpgradeUtility.Grant a real level-up screen already calls (see
    // LevelUpUtility.GrantOption). Lives on simulation state, so only a command (replicated like
    // input, executed on the same tick by every client) can mutate it and stay deterministic - a
    // direct call from the View would only ever run locally. Currently only sent by the debug
    // upgrade tester (View/Managers/PassiveUpgradeDebugTrigger.cs).
    public unsafe class GrantPassiveUpgradeCommand : DeterministicCommand
    {
        public AssetRef<PassiveUpgradeData> Upgrade;

        public override void Serialize(BitStream stream)
        {
            stream.Serialize(ref Upgrade);
        }
    }
}
