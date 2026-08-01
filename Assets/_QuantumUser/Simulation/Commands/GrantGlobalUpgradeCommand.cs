namespace Quantum
{
    using Photon.Deterministic;

    // Lets a client grant a Global Upgrade to itself outside the normal level-up flow - the same
    // GlobalUpgradeUtility.Grant a real level-up screen already calls (see
    // LevelUpUtility.GrantOption). Lives on simulation state, so only a command (replicated like
    // input, executed on the same tick by every client) can mutate it and stay deterministic - a
    // direct call from the View would only ever run locally. Currently only sent by the debug
    // upgrade tester (View/Managers/GlobalUpgradeDebugTrigger.cs).
    public unsafe class GrantGlobalUpgradeCommand : DeterministicCommand
    {
        public AssetRef<GlobalUpgradeData> Upgrade;

        public override void Serialize(BitStream stream)
        {
            stream.Serialize(ref Upgrade);
        }
    }
}
