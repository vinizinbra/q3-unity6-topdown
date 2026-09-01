namespace Quantum
{
    // Who a Rift Mutation actually affects - see RiftMutationData.Scope and docs/rift-mutations.md.
    //
    // The distinction is load-bearing in co-op, not just documentation: a Player-scope mutation
    // writes only its picker's own components, so two players independently picking it is fine and
    // expected. A Run-scope mutation writes shared simulation state (Frame.Global - see
    // RunMutations.qtn), so applying it twice would silently double a run-wide difficulty or
    // economy modifier. RiftMutationUtility.Grant/IsBlocked guard that via
    // Frame.Global.RunMutationPicks, which is why nothing else in the codebase has to think about
    // it.
    //
    // Player is deliberately value 0 - it's the overwhelmingly common case, so a mutation asset
    // authored without touching this field is correct by default.
    public enum MutationScope : byte
    {
        Player,
        Run
    }
}
