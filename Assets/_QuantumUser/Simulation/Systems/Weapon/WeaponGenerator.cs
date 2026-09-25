namespace Quantum
{
    // Rolls a weapon plus perkCount distinct perks off a WeaponPerkPoolData. Rolls on f.RNG, so
    // every client generates the same weapon on the same tick - never call this from view code.
    public static unsafe class WeaponGenerator
    {
        public static void Roll(Frame f, EntityRef owner, Weapon* weapon, AssetRef<WeaponDataAsset> weaponDataRef,
            AssetRef<WeaponPerkPoolData> poolRef, int perkCount)
        {
            ClearPerks(weapon);
            DrawPerks(f, weapon, weaponDataRef, poolRef, perkCount);

            // Bakes the drawn perks into the stats - has to come after the draw, not before.
            WeaponSystem.Equip(f, owner, weapon, weaponDataRef);
        }

        private static void ClearPerks(Weapon* weapon)
        {
            var perks = weapon->Perks;

            for (int i = 0; i < perks.Length; ++i)
            {
                perks[i] = default;
            }
        }

        private static void DrawPerks(Frame f, Weapon* weapon, AssetRef<WeaponDataAsset> weaponDataRef,
            AssetRef<WeaponPerkPoolData> poolRef, int perkCount)
        {
            DrawDistinctPerks(f, poolRef, perkCount, weapon->Perks, WeaponPerkTarget.Resolve(f, weaponDataRef));
        }

        // Weighted draw without replacement into `perks[0..slots)`, where slots = min(perkCount,
        // perks.Length): a drawn perk has its weight taken out of the running total, so one roll
        // can't contain the same perk twice. Stops early rather than repeating itself when the pool
        // holds fewer drawable perks than were asked for. Shared by WeaponGenerator.Roll (an
        // equipped weapon's own perk roster, above) and LevelUpUtility.RollWeaponOption (a
        // not-yet-equipped Choose-Weapon candidate's rolled perks) - same shape, different
        // destination buffer.
        public static int DrawDistinctPerks(Frame f, AssetRef<WeaponPerkPoolData> poolRef, int perkCount,
            FixedArray<AssetRef<WeaponPerkData>> perks, WeaponPerkTarget target)
        {
            if (poolRef.IsValid == false)
                return 0;

            WeaponPerkPoolData pool = f.FindAsset(poolRef);

            int slots = perkCount < perks.Length ? perkCount : perks.Length;

            if (perkCount > perks.Length)
                Log.Error($"[Weapon] asked for {perkCount} perks but the destination buffer only holds {perks.Length}");

            if (slots <= 0 || pool.Perks.Count == 0)
                return 0;

            bool* taken = stackalloc bool[pool.Perks.Count];
            int drawn = 0;

            for (int slot = 0; slot < slots; slot++)
            {
                // Re-totalled every slot rather than only subtracting the drawn weight - a drawn perk
                // can also make a DIFFERENT one ineligible (WeaponPerkData.ConflictsWith, checked
                // against the slots filled so far).
                int totalWeight = 0;

                for (int i = 0; i < pool.Perks.Count; i++)
                {
                    if (taken[i] == false)
                        totalWeight += GetWeight(f, pool, i, target, perks);
                }

                if (totalWeight <= 0)
                    break;

                int roll = f.RNG->Next(0, totalWeight);
                int cursor = 0;

                for (int i = 0; i < pool.Perks.Count; i++)
                {
                    if (taken[i])
                        continue;

                    int weight = GetWeight(f, pool, i, target, perks);

                    if (weight <= 0)
                        continue;

                    cursor += weight;

                    if (roll >= cursor)
                        continue;

                    taken[i] = true;
                    perks[slot] = pool.Perks[i];
                    drawn++;
                    break;
                }
            }

            Log.Debug($"[Weapon] rolled {drawn}/{slots} perks from {poolRef}");
            return drawn;
        }

        // Weight 0 is already the pool's own "not drawable" signal, so an ineligible perk reuses it
        // rather than needing its own skip at every loop that reads this.
        private static int GetWeight(Frame f, WeaponPerkPoolData pool, int index, in WeaponPerkTarget target,
            FixedArray<AssetRef<WeaponPerkData>> perks)
        {
            if (WeaponPerkEligibility.IsEligible(f, pool.Perks[index], target, perks) == false)
                return 0;

            return pool.GetWeight(f.FindAsset(pool.Perks[index]).Rarity);
        }
    }
}
