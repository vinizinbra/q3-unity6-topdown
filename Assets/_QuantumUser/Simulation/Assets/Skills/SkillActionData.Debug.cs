namespace Quantum
{
    // Debug-only surface for SkillActionData, split out so the core gameplay class (Phase/Priority/
    // Execute/ShouldExecute) isn't cluttered by test-only members. Uses Quantum's own
    // EditorButtonAttribute (Quantum.Engine.dll, already a precompiled reference of
    // Quantum.Simulation.asmdef) - not NaughtyAttributes' [Button], which this asmdef can't see, and
    // which would collide with Quantum's own Button (input) struct already in this namespace anyway.
    public abstract partial class SkillActionData
    {
        // Can't call QuantumRunner/SendCommand directly from here - Simulation must never reference
        // View (see architecture.md) - so these just raise SkillActionDataDebug.OnGrantRequested;
        // SkillUpgradeDebugTrigger (View/Managers/) subscribes and does the actual send. One button
        // per slot rather than a single button plus a slot field - only two valid targets exist, so
        // picking one is a single click instead of set-field-then-click.
        [EditorButton("Grant To Local Player (DashSkill)", EditorButtonVisibility.PlayMode)]
        protected void DebugGrantToLocalPlayerDashSkill()
        {
            SkillActionDataDebug.OnGrantRequested?.Invoke(this, SkillSlotId.DashSkill);
        }

        [EditorButton("Grant To Local Player (HeroSkill)", EditorButtonVisibility.PlayMode)]
        protected void DebugGrantToLocalPlayerHeroSkill()
        {
            SkillActionDataDebug.OnGrantRequested?.Invoke(this, SkillSlotId.HeroSkill);
        }
    }
}
