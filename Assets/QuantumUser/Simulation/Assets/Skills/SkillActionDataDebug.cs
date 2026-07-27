namespace Quantum
{
    using System;

    // View-side hook for SkillActionData's own "Grant To Local Player" debug buttons. Simulation
    // can't reach QuantumRunner/QuantumGame.SendCommand directly - the buttons just raise this
    // event; SkillUpgradeDebugTrigger (View/Managers/) subscribes and actually sends the
    // GrantSkillUpgradeCommand. Null until something subscribes, so the buttons silently no-op
    // if the scene has no trigger in it.
    public static class SkillActionDataDebug
    {
        public static Action<AssetRef<SkillActionData>, SkillSlotId> OnGrantRequested;
    }
}
