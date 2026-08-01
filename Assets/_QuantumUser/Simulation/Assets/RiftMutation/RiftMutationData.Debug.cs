namespace Quantum
{
    // Debug-only surface for RiftMutationData, split out so the core gameplay class (Apply) isn't
    // cluttered by test-only members. Mirrors GlobalUpgradeData.Debug.cs exactly - same
    // EditorButtonAttribute, same "raise an event, View sends the command" split (Simulation must
    // never reference View directly - see architecture.md).
    public abstract partial class RiftMutationData
    {
        // A single button (unlike SkillActionData's per-slot pair) - a Rift Mutation always applies
        // to the player itself, no slot to choose.
        [EditorButton("Grant To Local Player", EditorButtonVisibility.PlayMode)]
        protected void DebugGrantToLocalPlayer()
        {
            RiftMutationDataDebug.OnGrantRequested?.Invoke(this);
        }

        // No revert path exists: RiftMutationUtility.Grant -> Apply hand-mutates a component field
        // in place with no per-entity "currently granted" ledger to undo from - same reasoning
        // GlobalUpgradeData.Debug.cs's own Remove/Clear All buttons already document. Restart play
        // mode to actually reset a player.
        [EditorButton("Remove From Local Player", EditorButtonVisibility.PlayMode)]
        protected void DebugRemoveFromLocalPlayer()
        {
            Log.Error("[RiftMutationData] Remove not supported - mutations bake into live stats at grant time with no per-grant ledger to undo. Restart play mode to reset.");
        }

        [EditorButton("Clear All From Local Player", EditorButtonVisibility.PlayMode)]
        protected void DebugClearAllFromLocalPlayer()
        {
            Log.Error("[RiftMutationData] Clear All not supported - mutations bake into live stats at grant time with no per-grant ledger to undo. Restart play mode to reset.");
        }
    }
}
