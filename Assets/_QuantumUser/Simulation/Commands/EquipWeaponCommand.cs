namespace Quantum
{
    using Photon.Deterministic;

    // Lets a client equip any WeaponDataAsset onto its own player's Weapon outside the normal
    // drop/Store/Choose-Weapon flow - same WeaponSystem.Equip a real pickup/level-up choice screen
    // would call, so a weapon swapped in this way behaves identically (Ammo/FireCooldownTimer reset,
    // whatever perks are already on Weapon.Perks re-baked onto the new weapon). Weapon lives on
    // simulation state, so only a command (replicated like input, executed on the same tick by every
    // client) can mutate it and stay deterministic - a direct call from the View would only ever run
    // locally. Currently only sent by the debug weapon tester (View/Managers/WeaponDataDebugTrigger.cs).
    public unsafe class EquipWeaponCommand : DeterministicCommand
    {
        public AssetRef<WeaponDataAsset> WeaponData;

        public override void Serialize(BitStream stream)
        {
            stream.Serialize(ref WeaponData);
        }
    }
}
