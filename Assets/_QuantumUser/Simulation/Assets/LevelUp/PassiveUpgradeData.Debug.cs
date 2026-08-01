namespace Quantum
{
    // Debug-only surface for PassiveUpgradeData, split out so the core gameplay class (Description/
    // Apply) isn't cluttered by test-only members. Uses Quantum's own EditorButtonAttribute
    // (Quantum.Engine.dll, already a precompiled reference of Quantum.Simulation.asmdef) - not
    // NaughtyAttributes' [Button], which this asmdef can't see.
    public abstract partial class PassiveUpgradeData
    {
        // Can't call QuantumRunner/SendCommand directly from here - Simulation must never reference
        // View (see architecture.md) - so this just raises PassiveUpgradeDataDebug.OnGrantRequested;
        // PassiveUpgradeDebugTrigger (View/Managers/) subscribes and does the actual send. A single
        // button (unlike SkillActionData's per-slot pair) - a Passive Ascension always applies to the
        // player itself, no slot to choose.
        [EditorButton("Grant To Local Player", EditorButtonVisibility.PlayMode)]
        protected void DebugGrantToLocalPlayer()
        {
            PassiveUpgradeDataDebug.OnGrantRequested?.Invoke(this);
        }

        // No revert path exists: PassiveUpgradeUtility.Grant -> Apply hand-mutates a component field
        // in place (often additive-then-clamped) with no per-entity "currently granted" ledger to
        // undo from. These buttons exist for interface consistency with SkillActionData's real
        // Remove/Clear All, but can only log - restart play mode to actually reset a player.
        [EditorButton("Remove From Local Player", EditorButtonVisibility.PlayMode)]
        protected void DebugRemoveFromLocalPlayer()
        {
            Log.Error("[PassiveUpgradeData] Remove not supported - upgrades bake into live stats at grant time with no per-grant ledger to undo. Restart play mode to reset.");
        }

        [EditorButton("Clear All From Local Player", EditorButtonVisibility.PlayMode)]
        protected void DebugClearAllFromLocalPlayer()
        {
            Log.Error("[PassiveUpgradeData] Clear All not supported - upgrades bake into live stats at grant time with no per-grant ledger to undo. Restart play mode to reset.");
        }
    }
}
