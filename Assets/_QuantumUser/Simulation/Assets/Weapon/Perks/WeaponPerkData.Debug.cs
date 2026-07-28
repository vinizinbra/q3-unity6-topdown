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
    }
}
