namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;

    // Rolls/resolves the level-up upgrade-choice screen - see LevelUpSystem for the always-on driver
    // that calls into this, and docs/level-up-upgrades.md for the full runtime flow. Mirrors
    // ExperienceUtility's static-utility shape.
    public static unsafe class LevelUpUtility
    {
        private struct Candidate
        {
            public LevelUpOption Option;
            public int Weight;
        }

        // Called once by ExperienceUtility.Grant the instant Level increases (regardless of how many
        // levels a single Grant call covered - see that method's own comment). Rolls every currently
        // connected player's options and opens the screen, unless nobody got anything (every pool
        // empty), in which case there's nothing to show and the game just keeps going.
        public static void BeginLevelUpScreen(Frame f)
        {
            if (f.RuntimeConfig.LevelUpConfig.IsValid == false)
            {
                Log.Debug("[LevelUp] level-up reached but RuntimeConfig has no LevelUpConfig assigned - screen skipped");
                return;
            }

            LevelUpConfig config = f.FindAsset(f.RuntimeConfig.LevelUpConfig);
            List<EntityRef> recipients = new List<EntityRef>();

            var filtered = f.Filter<PlayerLink>();
            while (filtered.Next(out EntityRef entity, out PlayerLink _))
            {
                recipients.Add(entity);
                RestoreHealthAndShield(f, entity);
            }

            OpenUpgradeScreen(f, recipients, config, null);
        }

        // Leveling up fully refills every connected player's Health/Shield - a direct restore, same
        // "reposition without destroying" idiom DamageUtility.RespawnPlayer already uses for its own
        // full-refill moment, not a percent-of-max HealUtility/ShieldUtility grant - a level-up
        // shouldn't be reduced by a HealingReceivedMultiplier debuff, and this isn't a "number/
        // particle worth celebrating" heal source (see ShieldUtility's own comment on that
        // distinction). Fires unconditionally, even if every upgrade pool ends up empty below (see
        // OpenUpgradeScreen's anyRolled check) - the level itself was still earned.
        private static void RestoreHealthAndShield(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<Health>(entity, out var health) == true)
            {
                health->CurrentHealth = health->MaxHealth;
            }

            if (f.Unsafe.TryGetPointer<Shield>(entity, out var shield) == true)
            {
                shield->Current = shield->Max;
                shield->RechargeTimer = FP._0;
            }
        }

        // Called by ChestSystem the instant a player collects a Chest (see Chest.qtn/docs/chests.md)
        // - same roll-and-pause plumbing as a real level-up above, except only `player` gets a
        // LevelUpChoice, and the roll is forced to the Chest's own configured category instead of
        // following LevelUpConfig.LevelSequence.
        public static void BeginChestScreen(Frame f, EntityRef player, LevelUpCategory forcedCategory)
        {
            if (f.RuntimeConfig.LevelUpConfig.IsValid == false)
            {
                Log.Debug("[LevelUp] Chest opened but RuntimeConfig has no LevelUpConfig assigned - screen skipped");
                return;
            }

            LevelUpConfig config = f.FindAsset(f.RuntimeConfig.LevelUpConfig);
            OpenUpgradeScreen(f, new List<EntityRef> { player }, config, forcedCategory);
        }

        // Shared by BeginLevelUpScreen (every connected player, sequence-driven category) and
        // BeginChestScreen (one player, forced category) - both need the same "roll for these
        // recipients, then pause if anyone got anything" plumbing. The LevelUpScreenOpen guard here
        // is load-bearing, not just defensive: ChestSystem is a second, independent trigger of this
        // same flag alongside CurrencyOrbSystem/ExperienceUtility.Grant (see ChestSystem's own comment on
        // why it must stay outside GameplaySystemGroup), so re-entrancy is no longer prevented purely
        // by one caller being paused.
        private static void OpenUpgradeScreen(Frame f, List<EntityRef> recipients, LevelUpConfig config, LevelUpCategory? forcedCategory)
        {
            if (f.Global->LevelUpScreenOpen == true)
                return;

            bool anyRolled = false;
            int level = f.Global->Level + 1;

            for (int i = 0; i < recipients.Count; i++)
            {
                if (RollOptionsFor(f, recipients[i], config, forcedCategory, level) == true)
                    anyRolled = true;
            }

            if (anyRolled == false)
            {
                Log.Debug("[LevelUp] screen requested but every upgrade pool is empty for every recipient - skipped");
                return;
            }

            f.Global->LevelUpScreenOpen = true;
            f.Global->LevelUpTimeRemaining = config.DecisionTimeSeconds;
            f.SystemDisable<GameplaySystemGroup>();

            // Remember whatever state this interrupted (Survival, almost always - but a
            // talent-granted Chest can be opened while still in Lobby) so Resolve restores
            // exactly that, not a hardcoded Survival - see GameState.qtn's own Upgrade comment.
            f.Global->PreUpgradeState = f.Global->CurrentState;
            GameStateUtility.SetState(f, GameState.Upgrade);

            Log.Debug($"[LevelUp] screen opened at level {level} for {recipients.Count} recipient(s)");
        }

        // level is 1-based (the level about to be chosen FOR - see OpenUpgradeScreen's own
        // f.Global->Level + 1). Empty LevelSequence -> null (legacy mixed-all-categories roll, full
        // backward compat with an unedited LevelUpConfig.asset).
        private static LevelUpCategory? GetCategoryForLevel(LevelUpConfig config, int level)
        {
            if (config.LevelSequence == null || config.LevelSequence.Count == 0)
                return null;

            int index = (level - 1) % config.LevelSequence.Count;
            return config.LevelSequence[index];
        }

        // Weighted draw without replacement across whichever pool(s) `forcedCategory` (or the
        // level's own configured sequence slot) selects - same pattern as
        // WeaponGenerator.DrawDistinctPerks (draw, subtract the drawn candidate's weight, remove it,
        // repeat), stopping early if the combined pool holds fewer candidates than ChoiceCount asks
        // for.
        private static bool RollOptionsFor(Frame f, EntityRef entity, LevelUpConfig config, LevelUpCategory? forcedCategory, int level)
        {
            // All or Nothing (Rift Mutation) forces this entity's roll down to a single,
            // rarity-shifted option instead of the normal up-to-3 - see CharacterStats.
            // AllOrNothingActive and AddCandidate's own rarityShift handling below.
            bool allOrNothing = f.Unsafe.TryGetPointer<CharacterStats>(entity, out var rollingStats)
                && rollingStats->AllOrNothingActive == true;

            LevelUpCategory? category = forcedCategory ?? GetCategoryForLevel(config, level);

            // Only BeginChestScreen ever passes a non-null forcedCategory into OpenUpgradeScreen -
            // BeginLevelUpScreen always passes null, even when LevelUpConfig.LevelSequence forces a
            // category for this level. So this is the one signal the view needs to tell a Chest
            // screen's title (category name) apart from a plain level-up's (always generic).
            bool fromChest = forcedCategory.HasValue;

            // ChooseWeapon rolls a fundamentally different-shaped option (a whole weapon+perks combo,
            // not a single Rarity-weighted UpgradeData pick) - bypasses the weighted-Candidate
            // machinery below entirely rather than trying to force a shared weight onto it.
            if (category == LevelUpCategory.ChooseWeapon)
                return RollChooseWeaponOptionsFor(f, entity, config, fromChest);

            List<Candidate> candidates = new List<Candidate>();
            int totalWeight = 0;

            CollectCandidatesForCategory(f, entity, config, category, allOrNothing, candidates, ref totalWeight);

            // Configured category rolled dry (e.g. Hero Skill pool exhausted for this hero) - fall
            // back to the original mixed-all-categories roll for this player only, rather than
            // wasting their level-up on an empty screen.
            if (category != null && candidates.Count == 0)
            {
                CollectCandidatesForCategory(f, entity, config, null, allOrNothing, candidates, ref totalWeight);
            }

            int choiceCount = allOrNothing ? 1 : (config.ChoiceCount < 3 ? config.ChoiceCount : 3);
            LevelUpOption[] rolled = new LevelUpOption[choiceCount];
            int drawn = 0;

            for (int slot = 0; slot < choiceCount && totalWeight > 0 && candidates.Count > 0; slot++)
            {
                int roll = f.RNG->Next(0, totalWeight);
                int cursor = 0;
                int pick = candidates.Count - 1;

                for (int i = 0; i < candidates.Count; i++)
                {
                    cursor += candidates[i].Weight;

                    if (roll < cursor)
                    {
                        pick = i;
                        break;
                    }
                }

                Candidate candidate = candidates[pick];
                rolled[drawn] = candidate.Option;
                drawn++;

                totalWeight -= candidate.Weight;
                candidates.RemoveAt(pick);
            }

            if (drawn == 0)
            {
                f.Remove<LevelUpChoice>(entity);
                return false;
            }

            f.AddOrGet<LevelUpChoice>(entity, out var choice);
            var options = choice->Options;

            for (int i = 0; i < options.Length; i++)
            {
                options[i] = i < drawn ? rolled[i] : default;
            }

            choice->OptionCount = (byte)drawn;
            choice->Confirmed = false;
            choice->SelectedIndex = 0;
            choice->FromChest = fromChest;
            choice->Category = category ?? default;

            Log.Debug($"[LevelUp] rolled {drawn}/{choiceCount} option(s) for {entity}");
            return true;
        }

        // Every candidate, regardless of kind, is weighted the same way: resolve it generically as
        // UpgradeData and read its own Rarity via LevelUpConfig.GetWeight - no per-kind weighting
        // logic needed since WeaponPerkData/SkillActionData/GlobalUpgradeData/PassiveUpgradeData all
        // share that one field.
        private static void AddCandidate(Frame f, LevelUpConfig config, LevelUpPoolKind kind,
            AssetRef<UpgradeData> upgradeRef, SkillSlotId slot, bool rarityShift, List<Candidate> candidates, ref int totalWeight)
        {
            if (upgradeRef.IsValid == false)
                return;

            UpgradeData data = f.FindAsset(upgradeRef);

            // All or Nothing shifts the weight table up one tier (Common->Rare, Rare->Epic,
            // Epic->Legendary, Legendary stays) rather than filtering anything out outright - see
            // RollOptionsFor.
            UpgradeRarity effectiveRarity = rarityShift && data.Rarity < UpgradeRarity.Legendary
                ? data.Rarity + 1
                : data.Rarity;
            int weight = config.GetWeight(effectiveRarity);

            if (weight <= 0)
                return;

            LevelUpOption option = default;
            option.Kind = kind;
            option.Upgrade = upgradeRef;
            option.SkillUpgradeSlot = slot;

            candidates.Add(new Candidate { Option = option, Weight = weight });
            totalWeight += weight;
        }

        // Dispatches to exactly the collector(s) for `category`, or every collector except
        // ChooseWeapon (see RollOptionsFor) when `category` is null - the legacy "no sequence
        // configured"/fallback-on-empty-category mixed roll.
        private static void CollectCandidatesForCategory(Frame f, EntityRef entity, LevelUpConfig config,
            LevelUpCategory? category, bool rarityShift, List<Candidate> candidates, ref int totalWeight)
        {
            bool all = category == null;

            if (all || category == LevelUpCategory.WeaponPerk)
                CollectWeaponPerkCandidates(f, entity, config, rarityShift, candidates, ref totalWeight);

            if (all || category == LevelUpCategory.GlobalUpgrade)
                CollectGlobalUpgradeCandidates(f, entity, config, rarityShift, candidates, ref totalWeight);

            if (all || category == LevelUpCategory.RiftMutation)
                CollectRiftMutationCandidates(f, entity, config, rarityShift, candidates, ref totalWeight);

            if (all || category == LevelUpCategory.HeroSkill)
                CollectPerHeroCandidates(f, entity, config, rarityShift, candidates, ref totalWeight);
        }

        // AssetRef<WeaponPerkData> converts to AssetRef<UpgradeData> via its raw Id (same Guid, just
        // reinterpreted as the base type - see AssetRef<T>'s AssetGuid constructor). A perk already
        // sitting in one of this entity's own Weapon.Perks slots is excluded - offering it again
        // would just be a dead card, same reasoning AlreadyGranted uses for SkillUpgrade below.
        private static void CollectWeaponPerkCandidates(Frame f, EntityRef entity, LevelUpConfig config, bool rarityShift, List<Candidate> candidates, ref int totalWeight)
        {
            if (config.WeaponPerkPool.IsValid == false)
                return;

            WeaponPerkPoolData pool = f.FindAsset(config.WeaponPerkPool);
            f.Unsafe.TryGetPointer<Weapon>(entity, out var weapon);

            for (int i = 0; i < pool.Perks.Count; i++)
            {
                AssetRef<WeaponPerkData> perkRef = pool.Perks[i];

                if (weapon != null && AlreadyEquipped(weapon, perkRef) == true)
                    continue;

                AddCandidate(f, config, LevelUpPoolKind.WeaponPerk, new AssetRef<UpgradeData>(perkRef.Id), default, rarityShift, candidates, ref totalWeight);
            }
        }

        private static bool AlreadyEquipped(Weapon* weapon, AssetRef<WeaponPerkData> perkRef)
        {
            var perks = weapon->Perks;

            for (int i = 0; i < perks.Length; i++)
            {
                if (perks[i] == perkRef)
                    return true;
            }

            return false;
        }

        private static void CollectGlobalUpgradeCandidates(Frame f, EntityRef entity, LevelUpConfig config, bool rarityShift, List<Candidate> candidates, ref int totalWeight)
        {
            for (int i = 0; i < config.GlobalUpgrades.Count; i++)
            {
                AssetRef<GlobalUpgradeData> upgradeRef = config.GlobalUpgrades[i];

                if (IsCappedOut(f, entity, upgradeRef) == true)
                    continue;

                AddCandidate(f, config, LevelUpPoolKind.GlobalUpgrade, new AssetRef<UpgradeData>(upgradeRef.Id), default, rarityShift, candidates, ref totalWeight);
            }
        }

        // A GlobalUpgradeData authored with MaxPicks > 0 (e.g. Dash Charge) stops being offered to
        // this entity once it's already been picked that many times - offering it again would just
        // be a dead/wasted card, same reasoning as AlreadyGranted below for SkillUpgrade.
        private static bool IsCappedOut(Frame f, EntityRef entity, AssetRef<GlobalUpgradeData> upgradeRef)
        {
            if (upgradeRef.IsValid == false)
                return false;

            GlobalUpgradeData upgrade = f.FindAsset(upgradeRef);

            if (upgrade.MaxPicks <= 0)
                return false;

            return GlobalUpgradeUtility.GetPickCount(f, entity, upgradeRef) >= upgrade.MaxPicks;
        }

        // RiftMutation is a third globally-pooled kind alongside WeaponPerk/GlobalUpgrade above -
        // own list (LevelUpConfig.RiftMutations), own exclusion check (RiftMutationUtility.
        // IsAlreadyPicked rather than IsCappedOut, since non-stacking is pool-wide here, not a
        // per-asset MaxPicks). See docs/rift-mutations.md.
        private static void CollectRiftMutationCandidates(Frame f, EntityRef entity, LevelUpConfig config, bool rarityShift, List<Candidate> candidates, ref int totalWeight)
        {
            for (int i = 0; i < config.RiftMutations.Count; i++)
            {
                AssetRef<RiftMutationData> mutationRef = config.RiftMutations[i];

                if (RiftMutationUtility.IsAlreadyPicked(f, entity, mutationRef) == true)
                    continue;

                AddCandidate(f, config, LevelUpPoolKind.RiftMutation, new AssetRef<UpgradeData>(mutationRef.Id), default, rarityShift, candidates, ref totalWeight);
            }
        }

        // SkillUpgrade (CharacterData.DashSkillUpgrades, HeroSkill.Actions) and PassiveUpgrade
        // (CharacterData.PassiveUpgrades) are per-hero, not a shared config asset - which upgrades
        // make sense depends on which hero is rolling. Skill upgrades already present on the
        // matching slot are excluded - offering one that SkillSystem.AddUpgrade would just reject as
        // a duplicate is a dead card, not a real choice.
        private static void CollectPerHeroCandidates(Frame f, EntityRef entity, LevelUpConfig config, bool rarityShift, List<Candidate> candidates, ref int totalWeight)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false || stats->CharacterData.IsValid == false)
                return;

            CharacterData data = f.FindAsset(stats->CharacterData);
            f.Unsafe.TryGetPointer<CharacterSkills>(entity, out var skills);

            AddSkillUpgradeCandidates(f, entity, config, data.DashSkillUpgrades, SkillSlotId.DashSkill, skills, rarityShift, candidates, ref totalWeight);
            AddHeroSkillUpgradeCandidates(f, entity, config, data.HeroSkill, skills, rarityShift, candidates, ref totalWeight);

            for (int i = 0; i < data.PassiveUpgrades.Count; i++)
            {
                AssetRef<PassiveUpgradeData> upgradeRef = data.PassiveUpgrades[i];

                if (PassiveUpgradeUtility.IsAlreadyPicked(f, entity, upgradeRef) == true)
                    continue;

                if (f.FindAsset(upgradeRef).IsEligible(f, entity) == false)
                    continue;

                AddCandidate(f, config, LevelUpPoolKind.PassiveUpgrade, new AssetRef<UpgradeData>(upgradeRef.Id), default, rarityShift, candidates, ref totalWeight);
            }
        }

        private static void AddSkillUpgradeCandidates(Frame f, EntityRef entity, LevelUpConfig config, List<AssetRef<SkillActionData>> upgrades, SkillSlotId slotId,
            CharacterSkills* skills, bool rarityShift, List<Candidate> candidates, ref int totalWeight)
        {
            SkillSlot* slot = skills != null ? SkillSystem.ResolveSlot(skills, slotId) : null;

            for (int i = 0; i < upgrades.Count; i++)
            {
                AssetRef<SkillActionData> upgrade = upgrades[i];

                if (upgrade.IsValid == false || (slot != null && AlreadyGranted(f, entity, slot, upgrade) == true))
                    continue;

                if (f.FindAsset(upgrade).IsEligible(f, entity) == false)
                    continue;

                AddCandidate(f, config, LevelUpPoolKind.SkillUpgrade, new AssetRef<UpgradeData>(upgrade.Id), slotId, rarityShift, candidates, ref totalWeight);
            }
        }

        // No separate CharacterData.HeroSkillUpgrades list - the pool is HeroSkill's own Actions.
        // An entry authored there with Activated == false is exactly a "not running yet, offer it as
        // a pick" candidate (see SkillActionData.Activated and SkillSystem.InvokeActions' isUpgrade
        // bypass - granting it via AddUpgrade ignores Activated and turns it on for just this
        // player). An Activated == true entry is already running for every player with this hero
        // equipped, so it's excluded - there's nothing left to grant.
        private static void AddHeroSkillUpgradeCandidates(Frame f, EntityRef entity, LevelUpConfig config, AssetRef<SkillData> heroSkillRef,
            CharacterSkills* skills, bool rarityShift, List<Candidate> candidates, ref int totalWeight)
        {
            if (heroSkillRef.IsValid == false)
                return;

            SkillData heroSkill = f.FindAsset(heroSkillRef);
            SkillSlot* slot = skills != null ? SkillSystem.ResolveSlot(skills, SkillSlotId.HeroSkill) : null;

            for (int i = 0; i < heroSkill.Actions.Count; i++)
            {
                AssetRef<SkillActionData> actionRef = heroSkill.Actions[i];

                if (actionRef.IsValid == false || (slot != null && AlreadyGranted(f, entity, slot, actionRef) == true))
                    continue;

                SkillActionData action = f.FindAsset(actionRef);

                if (action.Activated == true)
                    continue;

                if (action.IsEligible(f, entity) == false)
                    continue;

                AddCandidate(f, config, LevelUpPoolKind.SkillUpgrade, new AssetRef<UpgradeData>(actionRef.Id), SkillSlotId.HeroSkill, rarityShift, candidates, ref totalWeight);
            }
        }

        // Rank-aware presence check - a ranked action (MaxRank > 1) stays offerable until it's been
        // picked MaxRank times (see SkillUpgradeUtility.IsCappedOut), instead of being excluded the
        // instant it's granted once. MaxRank defaults to 1, so this is a pure "is it in this slot's
        // Upgrades" boolean check for every non-ranked action, unchanged from before ranking existed.
        private static bool AlreadyGranted(Frame f, EntityRef entity, SkillSlot* slot, AssetRef<SkillActionData> upgrade)
        {
            SkillActionData action = f.FindAsset(upgrade);

            if (action.MaxRank > 1)
                return SkillUpgradeUtility.IsCappedOut(f, entity, upgrade);

            var granted = slot->Upgrades;

            for (int i = 0; i < granted.Length; i++)
            {
                if (granted[i] == upgrade)
                    return true;
            }

            return false;
        }

        // Rolls 3 (config.ChoiceCount, capped by pool size) DISTINCT weapons from
        // config.WeaponChoicePool, each with its own independently-rolled perk count/roster - a
        // fundamentally different shape from the weighted-Candidate-list draw every other category
        // uses (see RollOptionsFor), so this bypasses that machinery entirely instead of forcing a
        // shared weight onto a whole weapon+perks combo.
        private static bool RollChooseWeaponOptionsFor(Frame f, EntityRef entity, LevelUpConfig config, bool fromChest)
        {
            if (config.WeaponChoicePool.IsValid == false)
            {
                Log.Debug("[LevelUp] ChooseWeapon category configured but LevelUpConfig.WeaponChoicePool is unassigned - screen skipped for this entity");
                return false;
            }

            WeaponChoicePoolData pool = f.FindAsset(config.WeaponChoicePool);
            int poolCount = pool.Weapons.Count;
            int choiceCount = config.ChoiceCount < 3 ? config.ChoiceCount : 3;
            int slots = choiceCount < poolCount ? choiceCount : poolCount;

            if (slots <= 0)
            {
                f.Remove<LevelUpChoice>(entity);
                return false;
            }

            byte weaponTalentLevel = 0;
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == true)
                weaponTalentLevel = stats->WeaponTalentLevel;

            // Uniform draw-without-replacement of `slots` distinct weapon indices - no per-weapon
            // Rarity/weight axis (see WeaponChoicePoolData's own comment), unlike every other
            // category's weighted candidate draw. Always rolls the full `slots` real weapons - "keep
            // my current weapon" is a separate button/command (see ConfirmKeepCurrent), not one of
            // these 3 rolled options.
            bool* taken = stackalloc bool[poolCount];
            LevelUpOption[] rolled = new LevelUpOption[slots];

            for (int slot = 0; slot < slots; slot++)
            {
                int roll = f.RNG->Next(0, poolCount);

                while (taken[roll] == true)
                {
                    roll = (roll + 1) % poolCount;
                }

                taken[roll] = true;
                rolled[slot] = RollWeaponOption(f, config, pool.Weapons[roll], weaponTalentLevel);
            }

            f.AddOrGet<LevelUpChoice>(entity, out var choice);
            var options = choice->Options;

            for (int i = 0; i < options.Length; i++)
            {
                options[i] = i < slots ? rolled[i] : default;
            }

            choice->OptionCount = (byte)slots;
            choice->Confirmed = false;
            choice->SelectedIndex = 0;
            choice->KeptCurrent = false;
            choice->FromChest = fromChest;
            choice->Category = LevelUpCategory.ChooseWeapon;

            Log.Debug($"[LevelUp] rolled {slots} weapon choice(s) for {entity} at WeaponTalentLevel {weaponTalentLevel}");
            return true;
        }

        // Per-slot independent Bernoulli chance: slot i (0-based) succeeds with probability
        // clamp01((weaponTalentLevel - i) * ChancePerLevelPerSlot). The number of successes across
        // [0, MaxRolledPerks) is this weapon's rolled perk count - see LevelUpConfig's own comment
        // for the worked example this matches.
        private static LevelUpOption RollWeaponOption(Frame f, LevelUpConfig config, AssetRef<WeaponDataAsset> weaponRef, byte weaponTalentLevel)
        {
            int perkCount = 0;

            for (int slot = 0; slot < config.MaxRolledPerks; slot++)
            {
                FP chance = FPMath.Clamp01((weaponTalentLevel - slot) * config.ChancePerLevelPerSlot);

                if (DamageUtility.RollChance(f, chance) == true)
                    perkCount++;
            }

            LevelUpOption option = default;
            option.Kind = LevelUpPoolKind.ChooseWeapon;
            option.WeaponData = weaponRef;

            if (perkCount > 0 && config.WeaponPerkPool.IsValid == true)
            {
                int drawn = WeaponGenerator.DrawDistinctPerks(f, config.WeaponPerkPool, perkCount, option.RolledPerks);
                option.RolledPerkCount = (byte)drawn;
            }

            return option;
        }

        // Called from LevelUpSystem when a SelectLevelUpUpgradeCommand lands for this entity.
        public static void ConfirmSelection(Frame f, EntityRef entity, LevelUpChoice* choice, int optionIndex)
        {
            if (choice->Confirmed == true)
                return; // already locked in - a second click can't change the pick

            if (optionIndex < 0 || optionIndex >= choice->OptionCount)
            {
                Log.Error($"[LevelUp] {entity} sent OptionIndex {optionIndex}, outside 0-{choice->OptionCount - 1} - ignored");
                return;
            }

            choice->SelectedIndex = (byte)optionIndex;
            choice->Confirmed = true;

            Log.Debug($"[LevelUp] {entity} picked option {optionIndex} ({choice->Options[optionIndex].Kind})");
        }

        // Called from LevelUpSystem when a RerollLevelUpOptionsCommand lands for this entity. Spends
        // one CharacterStats.RerollQuantity charge to redraw this entity's own LevelUpChoice in
        // place, by simply calling RollOptionsFor again with the exact same inputs the original roll
        // used - level hasn't changed (the game is still paused, Frame.Global.Level only advances in
        // Resolve), so GetCategoryForLevel deterministically re-derives the same category a plain
        // level-up used without needing to store it; a Chest instead reuses choice->Category
        // directly (the forced category IS stored, via FromChest) rather than re-deriving anything.
        // RollOptionsFor already resets Confirmed/SelectedIndex and overwrites Options in place (via
        // f.AddOrGet finding the existing component), so a reroll needs no separate "clear" step.
        public static void RerollOptionsFor(Frame f, EntityRef entity, LevelUpChoice* choice, LevelUpConfig config)
        {
            if (choice->Confirmed == true)
                return; // already locked in - nothing left to reroll

            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false || stats->RerollQuantity <= 0)
            {
                Log.Debug($"[LevelUp] {entity} has no reroll charges remaining");
                return;
            }

            int level = f.Global->Level + 1;
            LevelUpCategory? forcedCategory = choice->FromChest ? (LevelUpCategory?)choice->Category : null;

            if (RollOptionsFor(f, entity, config, forcedCategory, level) == false)
                return; // pool ran dry - nothing to spend the charge on

            stats->RerollQuantity--;

            Log.Debug($"[LevelUp] {entity} rerolled - {stats->RerollQuantity} charge(s) left");
        }

        // Called from LevelUpSystem when a KeepCurrentWeaponCommand lands for this entity - the
        // separate "Keep Current" button on a Choose-Weapon screen, distinct from picking one of the
        // 3 rolled Options (see UpgradeWindow.keepCurrentButton). Doesn't check/require
        // Category == ChooseWeapon - a stray click during any other screen just confirms with
        // nothing granted, which is harmless (worst case the player skips a free upgrade), not
        // exploitable.
        public static void ConfirmKeepCurrent(Frame f, EntityRef entity, LevelUpChoice* choice)
        {
            if (choice->Confirmed == true)
                return; // already locked in

            choice->KeptCurrent = true;
            choice->Confirmed = true;

            Log.Debug($"[LevelUp] {entity} kept their current weapon");
        }

        // Random pick among the entity's own already-rolled options - shared by a mid-screen
        // disconnect and Resolve's own timeout fallback. Rolls over OptionCount, never
        // Options.Length - trailing slots past OptionCount are unrolled Kind.None.
        public static void AutoConfirm(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<LevelUpChoice>(entity, out var choice) == false)
                return;

            if (choice->Confirmed == true || choice->OptionCount == 0)
                return;

            choice->SelectedIndex = (byte)f.RNG->Next(0, choice->OptionCount);
            choice->Confirmed = true;

            Log.Debug($"[LevelUp] {entity} did not confirm - auto-picked option {choice->SelectedIndex}");
        }

        // Called by LevelUpSystem once every player has confirmed (or the countdown ran out).
        // Auto-picks for anyone still unconfirmed, grants every entity's chosen option, then closes
        // the screen and resumes gameplay.
        public static void Resolve(Frame f)
        {
            var filtered = f.Filter<PlayerLink>();

            while (filtered.Next(out EntityRef entity, out PlayerLink _))
            {
                if (f.Unsafe.TryGetPointer<LevelUpChoice>(entity, out var choice) == false)
                    continue;

                AutoConfirm(f, entity);

                // KeptCurrent (the separate "Keep Current" button) means a genuine no-op - nothing
                // to grant, same as if the player had picked nothing. AutoConfirm never sets this;
                // it's only ever true from an explicit ConfirmKeepCurrent call.
                if (choice->OptionCount > 0 && choice->KeptCurrent == false)
                {
                    GrantOption(f, entity, choice->Options[choice->SelectedIndex]);
                }

                f.Remove<LevelUpChoice>(entity);
            }

            f.Global->LevelUpScreenOpen = false;
            f.Global->LevelUpTimeRemaining = FP._0;
            f.SystemEnable<GameplaySystemGroup>();

            GameStateUtility.SetState(f, f.Global->PreUpgradeState);

            Log.Debug("[LevelUp] screen resolved - gameplay resumed");
        }

        // Upgrade is stored generically as AssetRef<UpgradeData> - Kind says which concrete grant
        // path applies, so the raw Id is reinterpreted into the AssetRef<T> each path actually needs
        // (same Guid, just typed differently - see AddCandidate's own comment).
        private static void GrantOption(Frame f, EntityRef entity, LevelUpOption option)
        {
            switch (option.Kind)
            {
                case LevelUpPoolKind.WeaponPerk:
                    if (f.Unsafe.TryGetPointer<Weapon>(entity, out var weapon) == true)
                        WeaponSystem.AddPerk(f, entity, weapon, new AssetRef<WeaponPerkData>(option.Upgrade.Id));
                    break;

                case LevelUpPoolKind.SkillUpgrade:
                {
                    // AddUpgrade can legitimately fail (both Upgrades[]/PendingUpgrades[] slots already
                    // full) - only record history when the grant actually landed, same as the debug
                    // menu's SkillSystem.ProcessGrantUpgradeCommand, so a pick can't show as granted in
                    // the party HUD/upgrade popup while CharacterSkills never actually got it.
                    bool granted = false;

                    if (f.Unsafe.TryGetPointer<CharacterSkills>(entity, out var skills) == true)
                    {
                        SkillSlot* slot = SkillSystem.ResolveSlot(skills, option.SkillUpgradeSlot);

                        if (slot != null)
                            granted = SkillSystem.AddUpgrade(f, slot, new AssetRef<SkillActionData>(option.Upgrade.Id));
                    }

                    if (granted == true)
                        RecordHistory(f, entity, option.Kind, option.Upgrade);

                    break;
                }

                case LevelUpPoolKind.GlobalUpgrade:
                    GlobalUpgradeUtility.Grant(f, entity, new AssetRef<GlobalUpgradeData>(option.Upgrade.Id));
                    RecordHistory(f, entity, option.Kind, option.Upgrade);
                    break;

                case LevelUpPoolKind.RiftMutation:
                    RiftMutationUtility.Grant(f, entity, new AssetRef<RiftMutationData>(option.Upgrade.Id));
                    RecordHistory(f, entity, option.Kind, option.Upgrade);
                    break;

                case LevelUpPoolKind.PassiveUpgrade:
                    PassiveUpgradeUtility.Grant(f, entity, new AssetRef<PassiveUpgradeData>(option.Upgrade.Id));
                    RecordHistory(f, entity, option.Kind, option.Upgrade);
                    break;

                case LevelUpPoolKind.ChooseWeapon:
                    // "Keep Current" (see ConfirmKeepCurrent) never reaches here - Resolve checks
                    // choice->KeptCurrent before calling GrantOption at all, so every option that
                    // does reach this case is a genuine rolled-weapon pick.
                    WeaponChoiceUtility.Grant(f, entity, option);
                    break;
            }
        }

        // Flat "everything this entity has ever picked" ledger, for the party HUD's icon row
        // (PartyHistoryUpgradeContainer) - see UpgradeHistory in LevelUp.qtn. Covers Skill Upgrade/
        // Global Upgrade/Passive Upgrade/Rift Mutation - Weapon Perk and ChooseWeapon are
        // deliberately excluded (already visible on the weapon itself, and roll too often/carry no
        // single UpgradeData ref to be worth a HUD icon). Independent of each covered kind's own
        // gameplay-facing tracking (SkillSlot.Upgrades, GlobalUpgradePicks, RiftMutationPicks). Same
        // find-or-add-slot idiom as GlobalUpgradeUtility.RecordPick.
        public static void RecordHistory(Frame f, EntityRef entity, LevelUpPoolKind kind, AssetRef<UpgradeData> upgrade)
        {
            if (kind == LevelUpPoolKind.WeaponPerk || kind == LevelUpPoolKind.ChooseWeapon)
                return;

            if (upgrade.IsValid == false)
                return;

            f.AddOrGet<UpgradeHistory>(entity, out var history);
            var entries = history->Entries;

            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].Upgrade != upgrade)
                    continue;

                UpgradeHistoryEntry entry = entries[i];
                entry.Count++;
                entries[i] = entry;
                return;
            }

            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].Upgrade.IsValid == true)
                    continue;

                entries[i] = new UpgradeHistoryEntry { Kind = kind, Upgrade = upgrade, Count = 1 };
                return;
            }

            Log.Error($"[LevelUp] {entity} has no free UpgradeHistory slot for {upgrade} - it won't show up in the upgrade icon row");
        }
    }
}
