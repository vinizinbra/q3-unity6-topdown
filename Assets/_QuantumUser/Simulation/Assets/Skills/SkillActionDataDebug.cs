namespace Quantum
{
    using System;

    // View-side hook for SkillActionData's own "Grant/Remove/Clear All" debug buttons. Simulation
    // can't reach QuantumRunner/QuantumGame.SendCommand directly - the buttons just raise these
    // events; SkillUpgradeDebugTrigger (View/Managers/) subscribes and actually sends the matching
    // command (Grant/RemoveSkillUpgradeCommand, ClearSkillUpgradesCommand). Null until something
    // subscribes, so the buttons silently no-op if the scene has no trigger in it.
    public static class SkillActionDataDebug
    {
        public static Action<AssetRef<SkillActionData>, SkillSlotId> OnGrantRequested;
        public static Action<AssetRef<SkillActionData>, SkillSlotId> OnRemoveRequested;
        public static Action<SkillSlotId> OnClearAllRequested;
    }
}
