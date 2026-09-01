namespace Quantum
{
    using Photon.Deterministic;

    // Grant path for LevelUpPoolKind.ChooseWeapon - see LevelUpUtility.GrantOption. Unlike
    // GlobalUpgradeUtility/RiftMutationUtility's single-asset dispatch, this equips a whole
    // weapon+perks combo, brings it up to its own rolled starting Level, and bumps the picking
    // entity's own CharacterStats.WeaponTalentLevel - a persistent in-run "how many Choose-Weapon
    // picks has this player made" counter, kept for bookkeeping even though it no longer drives the
    // roll itself (perk count/starting Level are rolled by LevelUpConfig.WeaponOfferCurve, keyed by
    // Global.SurvivalTime instead - see LevelUpUtility.RollWeaponOption).
    public static unsafe class WeaponChoiceUtility
    {
        public static void Grant(Frame f, EntityRef entity, LevelUpOption option, FP weaponLevelDamageBonusPerLevel)
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

            // Equip always resets Level back to 0 (WeaponSystem.SeedStats), so the rolled starting
            // Level has to be applied AFTER Equip, not baked into the option/weapon beforehand - same
            // ordering constraint Store's own purchase flow already had to work around.
            for (int i = 0; i < option.RolledWeaponLevel; i++)
            {
                WeaponSystem.AddLevel(weapon, weaponLevelDamageBonusPerLevel);
            }

            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == true)
            {
                stats->WeaponTalentLevel++;
            }

            Log.Debug($"[LevelUp] {entity} equipped {option.WeaponData} with {option.RolledPerkCount} perk(s) at Weapon Level {option.RolledWeaponLevel}");
        }
    }
}
