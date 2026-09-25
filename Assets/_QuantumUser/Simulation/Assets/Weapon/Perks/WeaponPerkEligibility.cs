namespace Quantum
{
    // The one "may this perk go on this weapon" check every draw site shares, so a new rule (like the
    // hit-type filter or a perk conflict) can't be added to one site and forgotten at another -
    // which is how Split Shot's old fire-type filter and the level-up's AlreadyEquipped check had
    // drifted apart across WeaponGenerator/LevelUpUtility/StoreUtility/BlacksmithUtility.
    public static unsafe class WeaponPerkEligibility
    {
        // `perks` is whatever the weapon already holds (a live Weapon.Perks, or the slots a roll has
        // filled so far) - skipped entirely when there's nothing to compare against yet.
        public static bool IsEligible(Frame f, AssetRef<WeaponPerkData> perkRef, in WeaponPerkTarget target,
            FixedArray<AssetRef<WeaponPerkData>> perks)
        {
            if (perkRef.IsValid == false)
                return false;

            WeaponPerkData perk = f.FindAsset(perkRef);

            if (perk == null || perk.SupportsWeapon(target) == false)
                return false;

            for (int i = 0; i < perks.Length; i++)
            {
                if (perks[i].IsValid == false)
                    continue;

                if (perks[i] == perkRef)
                    return false;

                WeaponPerkData owned = f.FindAsset(perks[i]);

                if (owned != null && (perk.ConflictsWith(owned) == true || owned.ConflictsWith(perk) == true))
                    return false;
            }

            return true;
        }

        public static bool IsEligible(Frame f, AssetRef<WeaponPerkData> perkRef, Weapon* weapon)
        {
            if (weapon == null)
                return IsEligible(f, perkRef, WeaponPerkTarget.Default, default);

            return IsEligible(f, perkRef, WeaponPerkTarget.Resolve(f, weapon->WeaponData), weapon->Perks);
        }
    }
}
