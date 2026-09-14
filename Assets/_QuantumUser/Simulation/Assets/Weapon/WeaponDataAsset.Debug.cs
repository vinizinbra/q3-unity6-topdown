namespace Quantum
{
    // Debug-only surface for WeaponDataAsset, split out so the core gameplay class isn't cluttered
    // by test-only members. Uses Quantum's own EditorButtonAttribute (Quantum.Engine.dll, already a
    // precompiled reference of Quantum.Simulation.asmdef) - not NaughtyAttributes' [Button], which
    // this asmdef can't see. Same split/reasoning as WeaponPerkData.Debug.cs.
    public partial class WeaponDataAsset
    {
        // Can't call QuantumRunner/SendCommand directly from here - Simulation must never reference
        // View (see architecture.md) - so this just raises WeaponDataAssetDebug.OnEquipRequested;
        // WeaponDataDebugTrigger (View/Managers/) subscribes and does the actual send. Equips THIS
        // asset via the normal WeaponSystem.Equip path - Ammo/cooldowns reset and whatever perks are
        // already on Weapon.Perks re-bake onto the new weapon, same as a real pickup/Store swap.
        [EditorButton("Equip To Local Player", EditorButtonVisibility.PlayMode)]
        private void DebugEquipToLocalPlayer()
        {
            WeaponDataAssetDebug.OnEquipRequested?.Invoke(this);
        }
    }
}
