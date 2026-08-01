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
            if (f.Global->LevelUpScreenOpen == true)
                return; // structurally shouldn't happen - ExpOrbSystem (Grant's only caller) is
                        // itself paused for the whole screen - but guard defensively anyway.

            if (f.RuntimeConfig.LevelUpConfig.IsValid == false)
            {
                Log.Debug("[LevelUp] level-up reached but RuntimeConfig has no LevelUpConfig assigned - screen skipped");
                return;
            }

            LevelUpConfig config = f.FindAsset(f.RuntimeConfig.LevelUpConfig);
            bool anyRolled = false;

            var filtered = f.Filter<PlayerLink>();
            while (filtered.Next(out EntityRef entity, out PlayerLink _))
            {
                if (RollOptionsFor(f, entity, config) == true)
                    anyRolled = true;
            }

            if (anyRolled == false)
            {
                Log.Debug("[LevelUp] level-up reached but every upgrade pool is empty - screen skipped");
                return;
            }

            f.Global->LevelUpScreenOpen = true;
            f.Global->LevelUpTimeRemaining = config.DecisionTimeSeconds;
            f.SystemDisable<GameplaySystemGroup>();

            Log.Debug($"[LevelUp] screen opened at level {f.Global->Level + 1}");
        }

        // Weighted draw without replacement across every pool - same pattern as
        // WeaponGenerator.DrawPerks (draw, subtract the drawn candidate's weight, remove it, repeat),
        // stopping early if the combined pool holds fewer candidates than ChoiceCount asks for.
        private static bool RollOptionsFor(Frame f, EntityRef entity, LevelUpConfig config)
        {
            List<Candidate> candidates = new List<Candidate>();
            int totalWeight = 0;

            // All or Nothing (Rift Mutation) forces this entity's roll down to a single,
            // rarity-shifted option instead of the normal up-to-3 - see CharacterStats.
            // AllOrNothingActive and AddCandidate's own rarityShift handling below.
            bool allOrNothing = f.Unsafe.TryGetPointer<CharacterStats>(entity, out var rollingStats)
                && rollingStats->AllOrNothingActive == true;

            CollectGlobalCandidates(f, entity, config, allOrNothing, candidates, ref totalWeight);
            CollectRiftMutationCandidates(f, entity, config, allOrNothing, candidates, ref totalWeight);
            CollectPerHeroCandidates(f, entity, config, allOrNothing, candidates, ref totalWeight);

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

        // WeaponPerk and GlobalUpgrade are one shared pool for every player - unlike the per-hero
        // pools below. AssetRef<WeaponPerkData>/AssetRef<GlobalUpgradeData> convert to
        // AssetRef<UpgradeData> via their raw Id (same Guid, just reinterpreted as the base type -
        // see AssetRef<T>'s AssetGuid constructor).
        private static void CollectGlobalCandidates(Frame f, EntityRef entity, LevelUpConfig config, bool rarityShift, List<Candidate> candidates, ref int totalWeight)
        {
            if (config.WeaponPerkPool.IsValid == true)
            {
                WeaponPerkPoolData pool = f.FindAsset(config.WeaponPerkPool);

                for (int i = 0; i < pool.Perks.Count; i++)
                {
                    AddCandidate(f, config, LevelUpPoolKind.WeaponPerk, new AssetRef<UpgradeData>(pool.Perks[i].Id), default, rarityShift, candidates, ref totalWeight);
                }
            }

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

            AddSkillUpgradeCandidates(f, config, data.DashSkillUpgrades, SkillSlotId.DashSkill, skills, rarityShift, candidates, ref totalWeight);
            AddHeroSkillUpgradeCandidates(f, config, data.HeroSkill, skills, rarityShift, candidates, ref totalWeight);

            for (int i = 0; i < data.PassiveUpgrades.Count; i++)
            {
                AddCandidate(f, config, LevelUpPoolKind.PassiveUpgrade, new AssetRef<UpgradeData>(data.PassiveUpgrades[i].Id), default, rarityShift, candidates, ref totalWeight);
            }
        }

        private static void AddSkillUpgradeCandidates(Frame f, LevelUpConfig config, List<AssetRef<SkillActionData>> upgrades, SkillSlotId slotId,
            CharacterSkills* skills, bool rarityShift, List<Candidate> candidates, ref int totalWeight)
        {
            SkillSlot* slot = skills != null ? SkillSystem.ResolveSlot(skills, slotId) : null;

            for (int i = 0; i < upgrades.Count; i++)
            {
                AssetRef<SkillActionData> upgrade = upgrades[i];

                if (upgrade.IsValid == false || (slot != null && AlreadyGranted(slot, upgrade) == true))
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
        private static void AddHeroSkillUpgradeCandidates(Frame f, LevelUpConfig config, AssetRef<SkillData> heroSkillRef,
            CharacterSkills* skills, bool rarityShift, List<Candidate> candidates, ref int totalWeight)
        {
            if (heroSkillRef.IsValid == false)
                return;

            SkillData heroSkill = f.FindAsset(heroSkillRef);
            SkillSlot* slot = skills != null ? SkillSystem.ResolveSlot(skills, SkillSlotId.HeroSkill) : null;

            for (int i = 0; i < heroSkill.Actions.Count; i++)
            {
                AssetRef<SkillActionData> actionRef = heroSkill.Actions[i];

                if (actionRef.IsValid == false || (slot != null && AlreadyGranted(slot, actionRef) == true))
                    continue;

                SkillActionData action = f.FindAsset(actionRef);

                if (action.Activated == true)
                    continue;

                AddCandidate(f, config, LevelUpPoolKind.SkillUpgrade, new AssetRef<UpgradeData>(actionRef.Id), SkillSlotId.HeroSkill, rarityShift, candidates, ref totalWeight);
            }
        }

        private static bool AlreadyGranted(SkillSlot* slot, AssetRef<SkillActionData> upgrade)
        {
            var granted = slot->Upgrades;

            for (int i = 0; i < granted.Length; i++)
            {
                if (granted[i] == upgrade)
                    return true;
            }

            return false;
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

                if (choice->OptionCount > 0)
                {
                    GrantOption(f, entity, choice->Options[choice->SelectedIndex]);
                }

                f.Remove<LevelUpChoice>(entity);
            }

            f.Global->LevelUpScreenOpen = false;
            f.Global->LevelUpTimeRemaining = FP._0;
            f.SystemEnable<GameplaySystemGroup>();

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
                        WeaponSystem.AddPerk(f, weapon, new AssetRef<WeaponPerkData>(option.Upgrade.Id));
                    break;

                case LevelUpPoolKind.SkillUpgrade:
                    if (f.Unsafe.TryGetPointer<CharacterSkills>(entity, out var skills) == true)
                    {
                        SkillSlot* slot = SkillSystem.ResolveSlot(skills, option.SkillUpgradeSlot);

                        if (slot != null)
                            SkillSystem.AddUpgrade(f, slot, new AssetRef<SkillActionData>(option.Upgrade.Id));
                    }
                    break;

                case LevelUpPoolKind.GlobalUpgrade:
                    GlobalUpgradeUtility.Grant(f, entity, new AssetRef<GlobalUpgradeData>(option.Upgrade.Id));
                    break;

                case LevelUpPoolKind.RiftMutation:
                    RiftMutationUtility.Grant(f, entity, new AssetRef<RiftMutationData>(option.Upgrade.Id));
                    break;

                case LevelUpPoolKind.PassiveUpgrade:
                    PassiveUpgradeUtility.Grant(f, entity, new AssetRef<PassiveUpgradeData>(option.Upgrade.Id));
                    break;
            }
        }
    }
}
