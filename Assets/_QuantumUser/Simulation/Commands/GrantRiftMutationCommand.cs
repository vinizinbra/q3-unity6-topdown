namespace Quantum
{
    using Photon.Deterministic;

    // Lets a client grant a Rift Mutation to itself outside the normal level-up flow - the same
    // RiftMutationUtility.Grant a real level-up screen already calls (see
    // LevelUpUtility.GrantOption). Lives on simulation state, so only a command (replicated like
    // input, executed on the same tick by every client) can mutate it and stay deterministic - a
    // direct call from the View would only ever run locally. Mirrors GrantGlobalUpgradeCommand
    // exactly. Currently only sent by the debug mutation tester (View/Managers/
    // RiftMutationDebugTrigger.cs).
    public unsafe class GrantRiftMutationCommand : DeterministicCommand
    {
        public AssetRef<RiftMutationData> Mutation;

        public override void Serialize(BitStream stream)
        {
            stream.Serialize(ref Mutation);
        }
    }
}
