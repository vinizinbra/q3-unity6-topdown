namespace Quantum
{
    // Permanently shrinks the sacrificing player's own Recoverable Accessory Guard capacity (see
    // AccessoryGuard.qtn/docs/accessory-guard.md) instead of spending a currency - a standalone
    // payment, not a blocked hit, so it goes through AccessoryGuardUtility.TrySacrificeMaxDurability
    // rather than TryBlock: no collectible pops off, no debris, no AccessoryBlocked event. This is a
    // real sacrifice, not a recoverable charge - a Merchant repair afterward can only ever buy back up
    // to the new, lower MaxDurability, never the original. Only eligible while the accessory is
    // actually worn with at least one charge left (see IsEligible) - Airborne/Dropped/Broken/Disabled
    // players never get this option offered.
    public unsafe class AccessoryOfferingSacrificeData : SacrificeDefinition
    {
        public override bool IsEligible(Frame f, EntityRef entity)
        {
            return f.Unsafe.TryGetPointer<AccessoryGuard>(entity, out var guard) == true
                && AccessoryGuardUtility.IsAvailable(f, entity) == true
                && guard->State == AccessoryGuardState.Equipped
                && guard->CurrentDurability > 0;
        }

        public override void ApplyCost(Frame f, EntityRef entity)
        {
            AccessoryGuardUtility.TrySacrificeMaxDurability(f, entity);
        }

        public override string BuildValuePreview(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<AccessoryGuard>(entity, out var guard) == false)
                return string.Empty;

            int afterMax = guard->MaxDurability - 1;
            int afterCurrent = guard->CurrentDurability > afterMax ? afterMax : guard->CurrentDurability;

            return $"ACCESSORY (PERMANENT)\n{guard->CurrentDurability}/{guard->MaxDurability} -> {afterCurrent}/{afterMax}";
        }
    }
}
