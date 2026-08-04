namespace Quantum
{
    // Grant path for LevelUpPoolKind.ChooseWeapon - see LevelUpUtility.GrantOption. Unlike
    // GlobalUpgradeUtility/RiftMutationUtility's single-asset dispatch, this equips a whole
    // weapon+perks combo and bumps the picking entity's own WeaponTalentLevel (CharacterStats) - the
    // one persistent stat driving future Choose-Weapon perk-count rolls, see
    // LevelUpUtility.RollWeaponOption.
    public static unsafe class WeaponChoiceUtility
    {
        public static void Grant(Frame f, EntityRef entity, LevelUpOption option)
        {
            if (f.Unsafe.TryGetPointer<Weapon>(entity, out var weapon) == false)
            {
                Log.Error($"[LevelUp] {entity} picked a ChooseWeapon option but has no Weapon component - not granted");
                return;
            }

            // Perks filled in BEFORE Equip - Equip's own ApplyPerks reads weapon->Perks, same
            // ordering WeaponGenerator.Roll already relies on.
            var perks = weapon->Perks;

            for (int i = 0; i < perks.Length; i++)
            {
                perks[i] = i < option.RolledPerkCount ? option.RolledPerks[i] : default;
            }

            WeaponSystem.Equip(f, entity, weapon, option.WeaponData);

            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == true)
            {
                stats->WeaponTalentLevel++;
            }

            Log.Debug($"[LevelUp] {entity} equipped {option.WeaponData} with {option.RolledPerkCount} perk(s)");
        }
    }
}
