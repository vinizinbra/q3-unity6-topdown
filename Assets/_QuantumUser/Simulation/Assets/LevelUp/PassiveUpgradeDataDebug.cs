namespace Quantum
{
    using System;

    // View-side hook for PassiveUpgradeData's own "Grant To Local Player" debug button. Simulation
    // can't reach QuantumRunner/QuantumGame.SendCommand directly - the button just raises this
    // event; PassiveUpgradeDebugTrigger (View/Managers/) subscribes and actually sends the
    // GrantPassiveUpgradeCommand. Null until something subscribes, so the button silently no-ops if
    // the scene has no trigger in it.
    public static class PassiveUpgradeDataDebug
    {
        public static Action<AssetRef<PassiveUpgradeData>> OnGrantRequested;
    }
}
