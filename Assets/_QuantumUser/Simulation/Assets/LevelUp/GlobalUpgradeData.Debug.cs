namespace Quantum
{
    // Debug-only surface for GlobalUpgradeData, split out so the core gameplay class (Apply) isn't
    // cluttered by test-only members. Uses Quantum's own EditorButtonAttribute (Quantum.Engine.dll,
    // already a precompiled reference of Quantum.Simulation.asmdef) - not NaughtyAttributes'
    // [Button], which this asmdef can't see.
    public abstract partial class GlobalUpgradeData
    {
        // Can't call QuantumRunner/SendCommand directly from here - Simulation must never reference
        // View (see architecture.md) - so this just raises GlobalUpgradeDataDebug.OnGrantRequested;
        // GlobalUpgradeDebugTrigger (View/Managers/) subscribes and does the actual send. A single
        // button (unlike SkillActionData's per-slot pair) - a Global Upgrade always applies to the
        // player itself, no slot to choose.
        [EditorButton("Grant To Local Player", EditorButtonVisibility.PlayMode)]
        protected void DebugGrantToLocalPlayer()
        {
            GlobalUpgradeDataDebug.OnGrantRequested?.Invoke(this);
        }

        // No revert path exists: GlobalUpgradeUtility.Grant -> Apply hand-mutates a component field
        // in place (often multiply-then-clamped, or additive-then-since-partially-consumed) with no
        // per-entity "currently granted" ledger to undo from. These buttons exist for interface
        // consistency with SkillActionData's real Remove/Clear All, but can only log - restart play
        // mode to actually reset a player.
        [EditorButton("Remove From Local Player", EditorButtonVisibility.PlayMode)]
        protected void DebugRemoveFromLocalPlayer()
        {
            Log.Error("[GlobalUpgradeData] Remove not supported - upgrades bake into live stats at grant time with no per-grant ledger to undo. Restart play mode to reset.");
        }

        [EditorButton("Clear All From Local Player", EditorButtonVisibility.PlayMode)]
        protected void DebugClearAllFromLocalPlayer()
        {
            Log.Error("[GlobalUpgradeData] Clear All not supported - upgrades bake into live stats at grant time with no per-grant ledger to undo. Restart play mode to reset.");
        }
    }
}
