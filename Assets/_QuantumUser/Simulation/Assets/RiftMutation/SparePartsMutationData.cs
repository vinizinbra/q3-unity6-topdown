namespace Quantum
{
    // One emergency Accessory comeback. Once per run, an Accessory that would be destroyed instead
    // comes straight back with a small amount of durability - bypassing the normal rule that a
    // destroyed Accessory has to wait for the next Breathing Break and be bought back.
    //
    // "Once per run, not reset at a Breathing Break, not re-armed by repairing or repurchasing" is
    // structural rather than policed: the grant hands out a fixed number of charges on the generic
    // AccessoryEmergencyReserve component, AccessoryGuardUtility consumes them, and nothing anywhere
    // ever refills them. The component simply runs out.
    //
    // The reserve itself is deliberately generic - it knows nothing about this mutation, so any
    // future "your accessory gets one more life" source reuses it as-is.
    public unsafe class SparePartsMutationData : RiftMutationData
    {
        public byte Charges = 1;
        public byte RestoreDurability = 2;

        public override void Apply(Frame f, EntityRef entity)
        {
            f.AddOrGet<AccessoryEmergencyReserve>(entity, out var reserve);

            // Take-the-stronger rather than additive - a second source of a reserve should not
            // silently stack into several comebacks.
            if (Charges > reserve->Charges)
                reserve->Charges = Charges;

            if (RestoreDurability > reserve->RestoreDurability)
                reserve->RestoreDurability = RestoreDurability;
        }

        protected override object[] DescriptionArgs => new object[] { RestoreDurability };
    }
}
