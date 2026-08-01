namespace Quantum
{
    using System;

    // View-side hook for GlobalUpgradeData's own "Grant To Local Player" debug button. Simulation
    // can't reach QuantumRunner/QuantumGame.SendCommand directly - the button just raises this
    // event; GlobalUpgradeDebugTrigger (View/Managers/) subscribes and actually sends the
    // GrantGlobalUpgradeCommand. Null until something subscribes, so the button silently no-ops if
    // the scene has no trigger in it.
    public static class GlobalUpgradeDataDebug
    {
        public static Action<AssetRef<GlobalUpgradeData>> OnGrantRequested;
    }
}
