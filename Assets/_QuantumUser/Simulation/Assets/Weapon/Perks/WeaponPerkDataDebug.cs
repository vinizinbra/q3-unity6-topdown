namespace Quantum
{
    using System;

    // View-side hook for WeaponPerkData's own "Grant To Local Player" debug button. Simulation
    // can't reach QuantumRunner/QuantumGame.SendCommand directly - the button just raises this
    // event; WeaponPerkDebugTrigger (View/Managers/) subscribes and actually sends the
    // GrantWeaponPerkCommand. Null until something subscribes, so the button silently no-ops if the
    // scene has no trigger in it.
    public static class WeaponPerkDataDebug
    {
        public static Action<AssetRef<WeaponPerkData>> OnGrantRequested;
    }
}
