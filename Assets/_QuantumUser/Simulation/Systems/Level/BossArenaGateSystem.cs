namespace Quantum
{
    using UnityEngine.Scripting;

    // Forces every BossArenaGate's own PhysicsCollider3D to start disabled the instant it's added,
    // regardless of what the Editor-authored prototype's IsEnabled checkbox says -
    // RunPhaseUtility.BeginBossEncounter is the only thing that ever turns one back on (see
    // BossEncounter.qtn's own comment). Removes the "forgot to uncheck IsEnabled on this one gate"
    // footgun entirely rather than relying on Editor discipline for every hand-placed gate.
    [Preserve]
    public unsafe class BossArenaGateSystem : SystemSignalsOnly, ISignalOnComponentAdded<BossArenaGate>
    {
        public void OnAdded(Frame f, EntityRef entity, BossArenaGate* gate)
        {
            if (f.Unsafe.TryGetPointer<PhysicsCollider3D>(entity, out var collider) == true)
            {
                collider->Enabled = false;
            }
            else
            {
                Log.Error($"[RunPhase] {entity} has a BossArenaGate tag but no PhysicsCollider3D - nothing to disable");
            }
        }
    }
}
