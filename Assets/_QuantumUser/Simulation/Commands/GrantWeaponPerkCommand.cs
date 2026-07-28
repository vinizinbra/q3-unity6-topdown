namespace Quantum
{
    using Photon.Deterministic;

    // Lets a client grant a perk to its own player's weapon outside the normal drop-roll flow - the
    // same WeaponSystem.AddPerk a future level-up/pickup choice screen would call. Perks lives on
    // simulation state, so only a command (replicated like input, executed on the same tick by every
    // client) can mutate it and stay deterministic - a direct call from the View would only ever run
    // locally. Currently only sent by the debug perk tester (View/Managers/WeaponPerkDebugTrigger.cs);
    // reused as-is once a real level-up screen exists.
    public unsafe class GrantWeaponPerkCommand : DeterministicCommand
    {
        public AssetRef<WeaponPerkData> Perk;

        public override void Serialize(BitStream stream)
        {
            stream.Serialize(ref Perk);
        }
    }
}
