namespace Quantum
{
    using System;

    // View-side hook for WeaponDataAsset's own "Equip To Local Player" debug button. Simulation
    // can't reach QuantumRunner/QuantumGame.SendCommand directly - the button just raises this
    // event; WeaponDataDebugTrigger (View/Managers/) subscribes and actually sends the
    // EquipWeaponCommand. Null until something subscribes, so the button silently no-ops if the
    // scene has no trigger in it. Same shape as WeaponPerkDataDebug.OnGrantRequested.
    public static class WeaponDataAssetDebug
    {
        public static Action<AssetRef<WeaponDataAsset>> OnEquipRequested;
    }
}
