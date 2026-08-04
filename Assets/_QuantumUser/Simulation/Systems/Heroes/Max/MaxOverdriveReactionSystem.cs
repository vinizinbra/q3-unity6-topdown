namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Uncontrolled Fury - every kill landed while Overdrive (Berserk) is active extends its
    // remaining duration slightly, up to a per-activation cap (UncontrolledFuryExtension). Separate
    // from MaxVendettaSystem.OnEntityKilled (Vendetta Rush's own extension there) since this reacts
    // to ANY kill, not specifically a Vendetta-consuming one - keeping the two independent lets
    // either upgrade be equipped alone. Unfiltered - resolved directly off OnEntityKilled's own
    // payload, same shape MaxVendettaSystem/MaxFireMasteryReactionSystem already use. Gated purely
    // by UncontrolledFuryExtension's presence.
    [Preserve]
    public unsafe class MaxOverdriveReactionSystem : SystemMainThread, ISignalOnEntityKilled
    {
        public override void Update(Frame f)
        {
        }

        public void OnEntityKilled(Frame f, EntityRef target, EntityRef owner, DamageSource source)
        {
            if (f.Unsafe.TryGetPointer<UncontrolledFuryExtension>(owner, out var fury) == false)
                return;

            FP remaining = fury->MaxExtension - fury->AccumulatedExtension;

            if (remaining <= FP._0)
                return; // already extended this activation by the full cap

            FP extension = FPMath.Min(fury->PerKillExtension, remaining);

            if (OverdriveUtility.TryExtend(f, owner, extension) == false)
                return;

            fury->AccumulatedExtension += extension;
        }
    }
}
