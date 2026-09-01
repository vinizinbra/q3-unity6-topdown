namespace Quantum
{
    using Photon.Deterministic;

    // You hit far harder while your Accessory is off your head - so the moment it saves you is also
    // the moment you become most dangerous, and walking back to pick it up costs you that power.
    //
    // Tracks NO state of its own: the condition is read straight off AccessoryGuard.State via
    // AccessoryGuardUtility.IsExposed at every damage resolution, so it reacts instantly to a block,
    // a landing, a recovery or a Merchant replacement with nothing to keep in sync.
    //
    // IsExposed deliberately returns false for a player with no Accessory system at all, which is
    // what stops Last Bastion (Accessory removed outright, state pinned Broken) from being a
    // permanent free +75%. The IsEligible override below is the other half of that: it stops the
    // mutation being OFFERED to such a player in the first place.
    public unsafe class NoSafetyNetMutationData : RiftMutationData
    {
        public FP DamageBonus = FP._0;

        public override bool IsEligible(Frame f, EntityRef entity) => AccessoryGuardUtility.IsAvailable(f, entity);

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->NoSafetyNetDamageBonus = FPMath.Max(stats->NoSafetyNetDamageBonus, DamageBonus);
        }

        protected override object[] DescriptionArgs => new object[] { DamageBonus.AsFloat * 100f };
    }
}
