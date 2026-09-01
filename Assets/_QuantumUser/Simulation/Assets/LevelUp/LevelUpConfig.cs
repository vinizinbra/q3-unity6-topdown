namespace Quantum
{
    using System;
    using System.Collections.Generic;
    using Photon.Deterministic;
    using UnityEngine;

    // Global tuning for the level-up upgrade-choice screen - see docs/level-up-upgrades.md,
    // LevelUpUtility and LevelUpSystem. Referenced via RuntimeConfig.LevelUpConfig.
    //
    // Only WeaponPerk and GlobalUpgrade are pooled globally here - SkillUpgrade and PassiveUpgrade
    // are per-hero instead (see CharacterData.DashSkillUpgrades/PassiveUpgrades and HeroSkill's own
    // Actions - LevelUpUtility.AddHeroSkillUpgradeCandidates), since which skill/passive upgrades
    // make sense depends on which hero is picking. Only WeaponPerk/RiftMutation candidates still
    // carry a Rarity to weight by (via GetWeight below) - SkillUpgrade/GlobalUpgrade/PassiveUpgrade
    // draw at a flat CommonWeight instead (see LevelUpUtility.ResolveWeight). GetWeight itself is
    // independent of WeaponPerkPoolData's own (differently-tuned) weights, which stay reserved for
    // the original drop-roll mechanic (see WeaponGenerator).
    //
    // LevelSequence/WeaponChoicePool/WeaponOfferCurve below configure the newer per-level category
    // locking (LevelUpCategory) and the ChooseWeapon category specifically - see LevelUpUtility.
    // GetCategoryForLevel/RollChooseWeaponOptionsFor and Chest.qtn (a Chest reuses this same config,
    // forced to its own single category).
    //
    // One row per anchor minute of Global.SurvivalTime - mirrors BalanceConfig.RunCurveAnchor/
    // Evaluate's own shape (linear interpolation between bracketing anchors, clamped flat outside
    // the authored range), scoped to weapon-offer scaling specifically rather than folded into the
    // shared BalanceConfig asset, since WeaponLevel/StartingPerkRolls aren't multipliers applied to
    // a baseline the way EnemyHp/EnemyDmg/DirectorBudget are - they're direct authored values. A
    // slot beyond either bracketing anchor's own authored StartingPerkRolls length reads as 0
    // ("not yet unlocked"), so anchors don't all need the same array length.
    [Serializable]
    public class WeaponOfferTimeAnchor
    {
        public int Minute;
        public byte WeaponLevel;
        public FP[] StartingPerkRolls;
    }

    public class LevelUpConfig : AssetObject
    {
        // How long players get to pick before LevelUpUtility.Resolve auto-picks for anyone
        // unconfirmed - see LevelUpSystem.Update.
        public FP DecisionTimeSeconds = 30;

        // How many options LevelUpUtility.RollOptionsFor tries to roll per player - the combined
        // pool may hold fewer, in which case LevelUpChoice.OptionCount ends up lower than this.
        public int ChoiceCount = 3;

        // Reuses WeaponPerkData's own existing pool type - see WeaponPerkPoolData/WeaponGenerator.
        // Only WeaponPerkPoolData.Perks (which perks are eligible) is read for level-up rolling;
        // its own weight fields are not - see GetWeight below.
        public AssetRef<WeaponPerkPoolData> WeaponPerkPool;

        // Plumbing only for now - ships empty until Global Upgrades are actually designed. See
        // GlobalUpgradeData/GlobalUpgradeUtility.
        [ExpandableAsset] public List<AssetRef<GlobalUpgradeData>> GlobalUpgrades = new();

        // Pooled globally same as GlobalUpgrades above, but a separate list/rarity axis - Rift
        // Mutations are non-stackable (see RiftMutationData/RiftMutationPicks), so they need their
        // own pick-history component rather than reusing GlobalUpgradePicks. See
        // docs/rift-mutations.md. This is the "core" 14-mutation pool (Glass Core, Heavy Arsenal,
        // ...) - Cursed Rift's own reward roll (LevelUpUtility.RollMutationOptions) deliberately
        // only ever draws from this list, not RiftMarkMutations below.
        [ExpandableAsset] public List<AssetRef<RiftMutationData>> RiftMutations = new();

        // A second, independently-rollable Rift Mutation pool (its own LevelUpPoolKind/
        // LevelUpCategory) - the 11 "Rift Mark content pool" mutations that apply Rift Mark on some
        // trigger (Critical Fracture, Last Stand, ...). Split from RiftMutations above so a designer
        // can pace/gate the two groups independently via LevelSequence. Shares RiftMutationPicks'
        // non-stack tracking with RiftMutations - both lists draw from the same RiftMutationData
        // catalog and never overlap. See docs/rift-mutations.md.
        [ExpandableAsset] public List<AssetRef<RiftMutationData>> RiftMarkMutations = new();

        [Header("Category sequence")]
        // Which single LevelUpCategory a given level is locked to - LevelUpUtility.
        // GetCategoryForLevel indexes this cyclically as sequence[(level - 1) % sequence.Count]
        // (level is 1-based - the level a player is currently choosing an upgrade FOR). Empty
        // (default) means "no sequence configured" - RollOptionsFor falls back to the original
        // mixed-all-categories roll for every level, so an unedited LevelUpConfig.asset keeps
        // behaving exactly as it does today. See docs/level-up-upgrades.md.
        public List<LevelUpCategory> LevelSequence = new();

        [Header("Choose Weapon")]
        // Pool LevelUpUtility.RollChooseWeaponOptionsFor draws 3 distinct weapons from. Ships
        // unassigned until Editor-authored.
        public AssetRef<WeaponChoicePoolData> WeaponChoicePool;

        // Weapon Level + starting-perk-count scaling for a freshly-rolled weapon offer, by
        // Global.SurvivalTime - shared by BOTH Store's weapon shop (StoreUtility.RollWeaponOffers)
        // and a Choose-Weapon level-up/Chest pick (RollWeaponOption/RollChooseWeaponOptionsFor), so
        // the two draw from the exact same random configuration rather than two independently-tuned
        // formulas (previously: Store scaled off Global.BreathingIndex via its own
        // StoreConfig.BreakWeaponConfig, Choose-Weapon scaled off the persistent CharacterStats.
        // WeaponTalentLevel via a clamp01 formula - see docs/store-blacksmith.md/
        // docs/level-up-upgrades.md for that history). Each StartingPerkRolls entry is an
        // INDEPENDENT Bernoulli chance for that slot - the number of successes is the rolled perk
        // count (see RollWeaponOfferPerkCount). Deliberately NOT folded into the shared BalanceConfig
        // asset - see WeaponOfferTimeAnchor's own comment above.
        public WeaponOfferTimeAnchor[] WeaponOfferCurve =
        {
            new() { Minute = 0, WeaponLevel = 0, StartingPerkRolls = new[] { FP._0_20 } },
            new() { Minute = 1, WeaponLevel = 0, StartingPerkRolls = new[] { FP.FromString("0.26"), FP.FromString("0.05") } },
            new() { Minute = 2, WeaponLevel = 1, StartingPerkRolls = new[] { FP.FromString("0.33"), FP.FromString("0.10") } },
            new() { Minute = 3, WeaponLevel = 1, StartingPerkRolls = new[] { FP.FromString("0.39"), FP.FromString("0.15") } },
            new() { Minute = 4, WeaponLevel = 1, StartingPerkRolls = new[] { FP.FromString("0.45"), FP._0_20 } },
            new() { Minute = 5, WeaponLevel = 1, StartingPerkRolls = new[] { FP._0_50, FP.FromString("0.25"), FP.FromString("0.05") } },
            new() { Minute = 6, WeaponLevel = 2, StartingPerkRolls = new[] { FP.FromString("0.55"), FP.FromString("0.30"), FP.FromString("0.10") } },
            new() { Minute = 7, WeaponLevel = 2, StartingPerkRolls = new[] { FP.FromString("0.60"), FP.FromString("0.35"), FP.FromString("0.15") } },
            new() { Minute = 8, WeaponLevel = 2, StartingPerkRolls = new[] { FP.FromString("0.65"), FP.FromString("0.40"), FP._0_20 } },
            new() { Minute = 9, WeaponLevel = 2, StartingPerkRolls = new[] { FP.FromString("0.69"), FP.FromString("0.45"), FP.FromString("0.25"), FP.FromString("0.05") } },
            new() { Minute = 10, WeaponLevel = 3, StartingPerkRolls = new[] { FP.FromString("0.73"), FP._0_50, FP.FromString("0.30"), FP.FromString("0.10") } },
            new() { Minute = 11, WeaponLevel = 3, StartingPerkRolls = new[] { FP.FromString("0.76"), FP.FromString("0.55"), FP.FromString("0.35"), FP.FromString("0.15") } },
            new() { Minute = 12, WeaponLevel = 3, StartingPerkRolls = new[] { FP.FromString("0.80"), FP.FromString("0.60"), FP.FromString("0.40"), FP._0_20 } },
        };

        // The damage-bonus-per-level a freshly-rolled RolledWeaponLevel > 0 is worth (see
        // WeaponChoiceUtility.Grant) - same "+5%, compounding" idiom StoreConfig.
        // WeaponLevelUpDamageBonusPerLevel already uses for its own, unrelated guaranteed
        // "Increase Weapon Level" purchase (that one levels up an ALREADY-EQUIPPED weapon and stays
        // its own separate Store-only feature/config field - see docs/store-blacksmith.md).
        public FP WeaponLevelDamageBonusPerLevel = FP._0_05;

        private void ResolveWeaponOfferBracket(FP survivalSeconds, out WeaponOfferTimeAnchor from, out WeaponOfferTimeAnchor to, out FP t)
        {
            WeaponOfferTimeAnchor first = WeaponOfferCurve[0];

            if (survivalSeconds <= FP._0)
            {
                from = first;
                to = first;
                t = FP._0;
                return;
            }

            WeaponOfferTimeAnchor last = WeaponOfferCurve[WeaponOfferCurve.Length - 1];
            FP lastSeconds = last.Minute * 60;

            if (survivalSeconds >= lastSeconds)
            {
                from = last;
                to = last;
                t = FP._0;
                return;
            }

            for (int i = 0; i < WeaponOfferCurve.Length - 1; i++)
            {
                WeaponOfferTimeAnchor a = WeaponOfferCurve[i];
                WeaponOfferTimeAnchor b = WeaponOfferCurve[i + 1];
                FP bSeconds = b.Minute * 60;

                if (survivalSeconds <= bSeconds)
                {
                    FP aSeconds = a.Minute * 60;
                    from = a;
                    to = b;
                    t = (survivalSeconds - aSeconds) / (bSeconds - aSeconds);
                    return;
                }
            }

            from = last;
            to = last;
            t = FP._0;
        }

        // Clamped flat below the first anchor (Minute 0) and above the last.
        public byte ResolveWeaponOfferLevel(FP survivalSeconds)
        {
            if (WeaponOfferCurve == null || WeaponOfferCurve.Length == 0)
                return 0;

            ResolveWeaponOfferBracket(survivalSeconds, out var from, out var to, out FP t);
            return (byte)FPMath.RoundToInt(FPMath.Lerp(from.WeaponLevel, to.WeaponLevel, t));
        }

        // Rolls a fresh weapon offer's starting perk count at the given SurvivalTime - each slot's
        // chance is linearly interpolated between the bracketing anchors' own StartingPerkRolls
        // (a slot past either anchor's authored array length reads as 0), then rolled independently.
        // The single shared roll both Store and Choose-Weapon/Chest call - see this field's own
        // comment above.
        public int RollWeaponOfferPerkCount(Frame f, FP survivalSeconds)
        {
            if (WeaponOfferCurve == null || WeaponOfferCurve.Length == 0)
                return 0;

            ResolveWeaponOfferBracket(survivalSeconds, out var from, out var to, out FP t);

            int fromSlots = from.StartingPerkRolls?.Length ?? 0;
            int toSlots = to.StartingPerkRolls?.Length ?? 0;
            int maxSlots = fromSlots > toSlots ? fromSlots : toSlots;

            int perkCount = 0;

            for (int slot = 0; slot < maxSlots; slot++)
            {
                FP fromChance = slot < fromSlots ? from.StartingPerkRolls[slot] : FP._0;
                FP toChance = slot < toSlots ? to.StartingPerkRolls[slot] : FP._0;
                FP chance = FPMath.Lerp(fromChance, toChance, t);

                if (chance > FP._0 && DamageUtility.RollChance(f, chance) == true)
                    perkCount++;
            }

            return perkCount;
        }

        [Header("Rarity weights")]
        public int CommonWeight = 100;
        public int RareWeight = 20;
        public int EpicWeight = 5;
        public int LegendaryWeight = 1;

        // 0 or less benches a rarity outright - it can never be drawn, without deleting anything
        // from any pool. Same shape as WeaponPerkPoolData.GetWeight, kept separate deliberately -
        // level-up pacing may want different tuning than raw drop rolls.
        public int GetWeight(UpgradeRarity rarity)
        {
            switch (rarity)
            {
                case UpgradeRarity.Common: return CommonWeight;
                case UpgradeRarity.Rare: return RareWeight;
                case UpgradeRarity.Epic: return EpicWeight;
                case UpgradeRarity.Legendary: return LegendaryWeight;
                default: return 0;
            }
        }
    }
}
