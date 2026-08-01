namespace Quantum
{
    using System;

    // View-side hook for RiftMutationData's own "Grant To Local Player" debug button. Simulation
    // can't reach QuantumRunner/QuantumGame.SendCommand directly - the button just raises this
    // event; RiftMutationDebugTrigger (View/Managers/) subscribes and actually sends the
    // GrantRiftMutationCommand. Null until something subscribes, so the button silently no-ops if
    // the scene has no trigger in it. Mirrors GlobalUpgradeDataDebug exactly.
    public static class RiftMutationDataDebug
    {
        public static Action<AssetRef<RiftMutationData>> OnGrantRequested;
    }
}
