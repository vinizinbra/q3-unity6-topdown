namespace Quantum
{
    // Debug-only surface for WeaponPerkData, split out so the core gameplay class (Rarity/Apply)
    // isn't cluttered by test-only members. Uses Quantum's own EditorButtonAttribute
    // (Quantum.Engine.dll, already a precompiled reference of Quantum.Simulation.asmdef) - not
    // NaughtyAttributes' [Button], which this asmdef can't see.
    public abstract partial class WeaponPerkData
    {
        // Can't call QuantumRunner/SendCommand directly from here - Simulation must never reference
        // View (see architecture.md) - so this just raises WeaponPerkDataDebug.OnGrantRequested;
        // WeaponPerkDebugTrigger (View/Managers/) subscribes and does the actual send. A single
        // button (unlike SkillActionData's per-slot pair) - a player has exactly one Weapon, so
        // there's no target to choose.
        [EditorButton("Grant To Local Player", EditorButtonVisibility.PlayMode)]
        protected void DebugGrantToLocalPlayer()
        {
            WeaponPerkDataDebug.OnGrantRequested?.Invoke(this);
        }

        // No revert path exists: WeaponSystem.AddPerk bakes a perk's effect directly into Weapon's
        // own fields once at equip time (see Weapon.qtn) with no per-grant ledger to undo, and
        // several perks are lossy once applied (multiply-then-clamp, one-shot queued effects like
        // Echo). These buttons exist for interface consistency with SkillActionData's real
        // Remove/Clear All, but can only log - restart play mode to actually reset a weapon.
        [EditorButton("Remove From Local Player", EditorButtonVisibility.PlayMode)]
        protected void DebugRemoveFromLocalPlayer()
        {
            Log.Error("[WeaponPerkData] Remove not supported - perks bake into Weapon at equip time with no per-grant ledger to undo. Restart play mode to reset.");
        }

        [EditorButton("Clear All From Local Player", EditorButtonVisibility.PlayMode)]
        protected void DebugClearAllFromLocalPlayer()
        {
            Log.Error("[WeaponPerkData] Clear All not supported - perks bake into Weapon at equip time with no per-grant ledger to undo. Restart play mode to reset.");
        }
    }
}
