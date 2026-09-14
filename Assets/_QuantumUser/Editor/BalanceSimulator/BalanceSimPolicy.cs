namespace QuantumUser.Editor.BalanceSimulator
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Photon.Deterministic;
    using Quantum;
    using static BalanceSimAssets;

    // The decisions a human makes during a run: level-up card picks (mirrors LevelUpUtility's
    // category sequence + weighted roll) and Breathing Break shopping (StoreUtility/
    // BlacksmithUtility pricing). GreedyDps picks whatever raises TotalDps the most.
    public class BalanceSimPolicy
    {
        private readonly BalanceSimAssets assets;
        private readonly BalanceSimScenario scenario;
        private readonly SimRng rng;

        public BalanceSimPolicy(BalanceSimAssets assets, BalanceSimScenario scenario, SimRng rng)
        {
            this.assets = assets;
            this.scenario = scenario;
            this.rng = rng;
        }

        private class Candidate
        {
            public string Name;
            public double Weight;
            public Action<SimPlayer> Apply;
        }

        // ---------------------------------------------------------------- level-ups

        // `level` is Global.Level after the increment (1 for the first level-up), same value
        // LevelUpUtility.GetCategoryForLevel receives.
        public void OnLevelUp(SimPlayer player, int level, double survivalTime)
        {
            LevelUpConfig config = assets.LevelUp;
            LevelUpCategory? category = null;

            if (config.LevelSequence != null && config.LevelSequence.Count > 0)
                category = config.LevelSequence[(level - 1) % config.LevelSequence.Count];

            player.LevelUpsTaken++;

            if (category == LevelUpCategory.ChooseWeapon)
            {
                ChooseWeapon(player, survivalTime, $"L{level}");
                return;
            }

            List<Candidate> candidates = Collect(player, category);

            if (candidates.Count == 0 && category != null)
                candidates = Collect(player, null);

            if (candidates.Count == 0)
                return;

            int choiceCount = Math.Min(3, Math.Max(1, config.ChoiceCount));
            List<Candidate> rolled = DrawWeighted(candidates, choiceCount);
            Pick(player, rolled, $"L{level} {category?.ToString() ?? "Any"}");
        }

        private List<Candidate> Collect(SimPlayer player, LevelUpCategory? category)
        {
            var list = new List<Candidate>();
            bool all = category == null;
            LevelUpConfig config = assets.LevelUp;

            if ((all || category == LevelUpCategory.WeaponPerk) && assets.LevelUpPerkPool != null && player.Weapon.HasFreeSlot)
            {
                foreach (WeaponPerkData perk in PerkCandidates(assets.LevelUpPerkPool, player))
                {
                    WeaponPerkData p = perk;
                    list.Add(new Candidate
                    {
                        Name = p.name,
                        Weight = config.GetWeight(p.Rarity),
                        Apply = pl => pl.Weapon.AddPerk(p, scenario.UnquantifiedPerkDpsValue),
                    });
                }
            }

            if (all || category == LevelUpCategory.GlobalUpgrade)
            {
                foreach (GlobalUpgradeData upgrade in assets.GlobalUpgrades)
                {
                    GlobalUpgradeData u = upgrade;
                    int picks = player.GlobalPicks.TryGetValue(u, out int c) ? c : 0;
                    if (u.MaxPicks > 0 && picks >= u.MaxPicks)
                        continue;

                    list.Add(new Candidate
                    {
                        Name = u.name,
                        Weight = config.CommonWeight,
                        Apply = pl =>
                        {
                            pl.Stats.ApplyGlobalUpgrade(u);
                            pl.GlobalPicks[u] = (pl.GlobalPicks.TryGetValue(u, out int n) ? n : 0) + 1;
                        },
                    });
                }
            }

            if (all || category == LevelUpCategory.HeroSkill)
            {
                CharacterData data = player.Data;
                AddRanked(list, player, data.DashSkillUpgrades.Select(assets.Resolve), config.CommonWeight, "dash");

                SkillData heroSkill = assets.Resolve(data.HeroSkill);
                if (heroSkill != null)
                    AddRanked(list, player, heroSkill.Actions.Select(assets.Resolve).Where(a => a != null && a.Activated == false), config.CommonWeight, "skill");

                AddRanked(list, player, data.PassiveUpgrades.Select(assets.Resolve), config.CommonWeight, "passive");
            }

            // RiftMutation: rare run-wide picks with no quantifiable stat - skipped by the sim.
            return list;
        }

        private void AddRanked(List<Candidate> list, SimPlayer player, IEnumerable<UpgradeData> upgrades, int weight, string kind)
        {
            foreach (UpgradeData upgrade in upgrades)
            {
                if (upgrade == null)
                    continue;

                int maxRank = upgrade is IRankedUpgrade ranked ? Math.Max(1, (int)ranked.MaxRank) : 1;
                int rank = player.SkillRanks.TryGetValue(upgrade, out int r) ? r : 0;
                if (rank >= maxRank)
                    continue;

                UpgradeData u = upgrade;
                UpgradeValue delta = default;
                string label = $"{u.name} ({kind})";

                if (u is SkillActionData action)
                {
                    UpgradeValue next = BalanceSimSkillModel.EvaluateUpgrade(action, rank + 1, player.Skill, scenario, assets);
                    UpgradeValue current = rank > 0 ? BalanceSimSkillModel.EvaluateUpgrade(action, rank, player.Skill, scenario, assets) : default;
                    delta.SkillDamage = Math.Max(0, next.SkillDamage - current.SkillDamage);
                    delta.WeaponBonus = Math.Max(0, next.WeaponBonus - current.WeaponBonus);

                    if (delta.Quantified)
                        label += delta.SkillDamage > 0 ? $" +{delta.SkillDamage:0}/cast" : $" +{delta.WeaponBonus:P0} weapon";
                }

                list.Add(new Candidate
                {
                    Name = label,
                    Weight = weight,
                    Apply = pl =>
                    {
                        if (delta.Quantified)
                        {
                            pl.SkillUpgradeDamage += delta.SkillDamage;
                            pl.WeaponBuffBonus += delta.WeaponBonus;
                        }
                        else
                        {
                            pl.SkillUpgradeBonus += scenario.SkillUpgradeDpsValue;
                        }

                        pl.SkillRanks[u] = (pl.SkillRanks.TryGetValue(u, out int n) ? n : 0) + 1;
                    },
                });
            }
        }

        private IEnumerable<WeaponPerkData> PerkCandidates(WeaponPerkPoolData pool, SimPlayer player)
        {
            WeaponFireType fireType = player.Weapon.Data != null ? player.Weapon.Data.FireType : WeaponFireType.Projectile;

            foreach (AssetRef<WeaponPerkData> perkRef in pool.Perks)
            {
                WeaponPerkData perk = assets.Resolve(perkRef);
                if (perk == null || player.Weapon.HasPerk(perk) || perk.SupportsFireType(fireType) == false)
                    continue;

                yield return perk;
            }
        }

        // Mirrors LevelUpUtility.DrawWeighted - weighted draw without replacement.
        private List<Candidate> DrawWeighted(List<Candidate> candidates, int count)
        {
            var pool = new List<Candidate>(candidates);
            var drawn = new List<Candidate>();

            while (drawn.Count < count && pool.Count > 0)
            {
                double total = pool.Sum(c => Math.Max(0, c.Weight));
                double roll = rng.Next01() * total;
                Candidate chosen = pool[pool.Count - 1];
                double cumulative = 0;

                foreach (Candidate c in pool)
                {
                    cumulative += Math.Max(0, c.Weight);
                    if (roll < cumulative)
                    {
                        chosen = c;
                        break;
                    }
                }

                pool.Remove(chosen);
                drawn.Add(chosen);
            }

            return drawn;
        }

        private void Pick(SimPlayer player, List<Candidate> rolled, string context)
        {
            Candidate best = rolled[0];

            if (scenario.LevelUpPolicy == LevelUpPickPolicy.Random)
            {
                best = rolled[rng.Next(rolled.Count)];
            }
            else
            {
                double bestDps = double.NegativeInfinity;

                foreach (Candidate c in rolled)
                {
                    SimPlayer preview = player.Clone();
                    c.Apply(preview);
                    double dps = preview.TotalDps(scenario);

                    if (dps > bestDps)
                    {
                        bestDps = dps;
                        best = c;
                    }
                }
            }

            best.Apply(player);
            player.PickLog.Add($"{context}: {best.Name}");
        }

        // ---------------------------------------------------------------- weapons

        // Mirrors LevelUpUtility.RollChooseWeaponOptionsFor + WeaponChoiceUtility.Grant: 3 distinct
        // pool weapons at the current WeaponOfferCurve level/perk count; keep current if none is better.
        private void ChooseWeapon(SimPlayer player, double survivalTime, string context)
        {
            WeaponChoicePoolData pool = assets.WeaponChoicePool;
            if (pool == null || pool.Weapons.Count == 0)
                return;

            int slots = Math.Min(Math.Min(3, Math.Max(1, assets.LevelUp.ChoiceCount)), pool.Weapons.Count);
            var taken = new HashSet<int>();
            SimWeapon best = null;
            double bestDps = player.WeaponDps(scenario);

            for (int slot = 0; slot < slots; slot++)
            {
                int roll = rng.Next(pool.Weapons.Count);
                while (taken.Contains(roll))
                    roll = (roll + 1) % pool.Weapons.Count;
                taken.Add(roll);

                WeaponDataAsset data = assets.Resolve(pool.Weapons[roll]);
                if (data == null)
                    continue;

                SimWeapon offer = RollOffer(data, survivalTime, assets.LevelUpPerkPool, null);
                double dps = OfferDps(player, offer);

                if (dps > bestDps)
                {
                    bestDps = dps;
                    best = offer;
                }
            }

            if (best != null)
            {
                player.Weapon = best;
                player.PickLog.Add($"{context}: weapon {best.Name} L{best.Level} +{best.Perks.Count}p");
            }
            else
            {
                player.PickLog.Add($"{context}: kept weapon");
            }
        }

        private double OfferDps(SimPlayer player, SimWeapon offer)
        {
            SimPlayer preview = player.Clone();
            preview.Weapon = offer;
            return preview.WeaponDps(scenario);
        }

        // Level and perk count from LevelUpConfig.WeaponOfferCurve (shared by Store and Choose
        // Weapon); perks drawn distinct from the pool, by rarity weights (pool's own, or the
        // Store's talent tuning when given).
        private SimWeapon RollOffer(WeaponDataAsset data, double survivalTime, WeaponPerkPoolData perkPool, Func<UpgradeRarity, int> rarityWeight)
        {
            LevelUpConfig config = assets.LevelUp;
            FP seconds = FP.FromFloat_UNSAFE((float)survivalTime);
            int level = config.ResolveWeaponOfferLevel(seconds);
            var weapon = SimWeapon.Create(data, level, D(config.WeaponLevelDamageBonusPerLevel), assets);
            int perkCount = RollPerkCount(config, survivalTime);

            if (perkCount > 0 && perkPool != null)
            {
                var available = new List<WeaponPerkData>();
                foreach (AssetRef<WeaponPerkData> perkRef in perkPool.Perks)
                {
                    WeaponPerkData perk = assets.Resolve(perkRef);
                    if (perk != null && perk.SupportsFireType(data.FireType))
                        available.Add(perk);
                }

                for (int i = 0; i < perkCount && available.Count > 0; i++)
                {
                    WeaponPerkData perk = DrawPerk(available, p => rarityWeight != null ? rarityWeight(p.Rarity) : perkPool.GetWeight(p.Rarity));
                    available.Remove(perk);
                    weapon.AddPerk(perk, scenario.UnquantifiedPerkDpsValue);
                }
            }

            return weapon;
        }

        // Mirrors LevelUpConfig.RollWeaponOfferPerkCount (independent Bernoulli per slot, lerped
        // between anchors).
        private int RollPerkCount(LevelUpConfig config, double survivalTime)
        {
            int count = 0;
            foreach (double chance in PerkSlotChances(config, survivalTime))
                if (rng.Chance(chance))
                    count++;
            return count;
        }

        private static List<double> PerkSlotChances(LevelUpConfig config, double survivalTime)
        {
            var chances = new List<double>();
            WeaponOfferTimeAnchor[] curve = config.WeaponOfferCurve;
            if (curve == null || curve.Length == 0)
                return chances;

            WeaponOfferTimeAnchor from = curve[0];
            WeaponOfferTimeAnchor to = curve[0];
            double t = 0;

            if (survivalTime >= curve[^1].Minute * 60)
            {
                from = to = curve[^1];
            }
            else if (survivalTime > 0)
            {
                for (int i = 0; i < curve.Length - 1; i++)
                {
                    double a = curve[i].Minute * 60;
                    double b = curve[i + 1].Minute * 60;
                    if (survivalTime <= b)
                    {
                        from = curve[i];
                        to = curve[i + 1];
                        t = b > a ? (survivalTime - a) / (b - a) : 0;
                        break;
                    }
                }
            }

            int fromSlots = from.StartingPerkRolls?.Length ?? 0;
            int toSlots = to.StartingPerkRolls?.Length ?? 0;

            for (int slot = 0; slot < Math.Max(fromSlots, toSlots); slot++)
            {
                double fromChance = slot < fromSlots ? D(from.StartingPerkRolls[slot]) : 0;
                double toChance = slot < toSlots ? D(to.StartingPerkRolls[slot]) : 0;
                chances.Add(fromChance + (toChance - fromChance) * t);
            }

            return chances;
        }

        // Expected price of the designed per-Break loop (1 weapon + 1 perk + 1 accessory repair +
        // 1 food) at this point of the run - the affordability target the report compares wallets to.
        public double ExpectedLoopCost(double survivalTime, int breathingIndex)
        {
            double cost = 0;
            StoreConfig store = assets.Store;

            if (store != null)
            {
                double expectedPerks = PerkSlotChances(assets.LevelUp, survivalTime).Sum();
                cost += D(store.WeaponOfferBasePrice) + D(store.WeaponOfferPricePerPerk) * expectedPerks;

                if (store.OfferAccessoryService)
                    cost += D(store.ResolveAccessoryRepairCost(2));
            }

            BlacksmithConfig forge = assets.Blacksmith;
            if (forge != null)
            {
                BlacksmithBreakTuning tuning = forge.ResolveBreakTuning(breathingIndex);
                double weights = 0, weighted = 0;
                foreach (UpgradeRarity rarity in new[] { UpgradeRarity.Common, UpgradeRarity.Rare, UpgradeRarity.Epic, UpgradeRarity.Legendary })
                {
                    double w = Math.Max(0, tuning.GetWeight(rarity));
                    weights += w;
                    weighted += w * D(forge.ResolvePerkPrice(rarity));
                }
                cost += weights > 0 ? weighted / weights : D(forge.CommonPerkPrice);
            }

            FoodOfferPoolData foods = assets.FoodPool;
            if (foods != null && foods.Foods != null)
            {
                double weights = 0, weighted = 0;
                foreach (AssetRef<FoodOfferData> foodRef in foods.Foods)
                {
                    FoodOfferData food = assets.Resolve(foodRef);
                    if (food == null)
                        continue;
                    double w = Math.Max(0, food.Weight);
                    weights += w;
                    weighted += w * D(food.Price);
                }
                cost += weights > 0 ? weighted / weights : 0;
            }

            return cost;
        }

        private WeaponPerkData DrawPerk(List<WeaponPerkData> available, Func<WeaponPerkData, int> weightOf)
        {
            double total = available.Sum(p => Math.Max(0, weightOf(p)));
            if (total <= 0)
                return available[rng.Next(available.Count)];

            double roll = rng.Next01() * total;
            double cumulative = 0;

            foreach (WeaponPerkData perk in available)
            {
                cumulative += Math.Max(0, weightOf(perk));
                if (roll < cumulative)
                    return perk;
            }

            return available[^1];
        }

        // ---------------------------------------------------------------- Breathing Break shopping

        // The designed per-Break loop: weapon, perk, accessory repair, food - each only if affordable.
        public void OnBreathingBreak(SimPlayer player, double survivalTime, int breathingIndex)
        {
            Shop(player, survivalTime, breathingIndex);
            Blacksmith(player, breathingIndex);
            AccessoryRepair(player, breathingIndex);
            Food(player, breathingIndex);
        }

        private void AccessoryRepair(SimPlayer player, int breathingIndex)
        {
            StoreConfig store = assets.Store;
            if (store == null || store.OfferAccessoryService == false)
                return;

            // Durability lost per combat round isn't simulated - assume a mid repair (2 missing).
            double price = D(store.ResolveAccessoryRepairCost(2));

            for (int i = 0; i < scenario.AccessoryRepairsPerBreak; i++)
            {
                if (price <= 0 || Spend(player, price) == false)
                    break;

                player.PickLog.Add($"Break{breathingIndex}: accessory repair for {price:0}");
            }
        }

        private void Food(SimPlayer player, int breathingIndex)
        {
            FoodOfferPoolData pool = assets.FoodPool;
            if (pool == null || pool.Foods == null || pool.Foods.Count == 0)
                return;

            var foods = new List<FoodOfferData>();
            foreach (AssetRef<FoodOfferData> foodRef in pool.Foods)
            {
                FoodOfferData food = assets.Resolve(foodRef);
                if (food != null)
                    foods.Add(food);
            }

            if (foods.Count == 0)
                return;

            for (int i = 0; i < scenario.FoodBuysPerBreak; i++)
            {
                double total = foods.Sum(f => Math.Max(0, f.Weight));
                double roll = rng.Next01() * total;
                double cumulative = 0;
                FoodOfferData offer = foods[foods.Count - 1];

                foreach (FoodOfferData food in foods)
                {
                    cumulative += Math.Max(0, food.Weight);
                    if (roll < cumulative)
                    {
                        offer = food;
                        break;
                    }
                }

                double price = D(offer.Price);
                if (Spend(player, price) == false)
                    break;

                player.PickLog.Add($"Break{breathingIndex}: food {offer.name} for {price:0}");
            }
        }

        private double Spendable(SimPlayer player) => player.Coins - scenario.CoinReserve;

        private bool Spend(SimPlayer player, double price)
        {
            if (Spendable(player) < price)
                return false;

            player.Coins -= price;
            player.CoinsSpent += price;
            return true;
        }

        private void Shop(SimPlayer player, double survivalTime, int breathingIndex)
        {
            StoreConfig store = assets.Store;
            if (store == null || assets.StoreWeaponPool == null || assets.StoreWeaponPool.Weapons.Count == 0)
                return;

            // ShopWeaponOfferCount talent is 0 for a fresh account -> 1 visible offer.
            int offerCount = Math.Min(1, Math.Max(1, store.MaxWeaponOfferSlots));
            int bought = 0;
            WeaponTalentRarityTuning tuning = store.ResolveTalentRarityTuning(0);
            var taken = new HashSet<int>();
            double currentDps = player.WeaponDps(scenario);

            for (int i = 0; i < offerCount; i++)
            {
                int roll = rng.Next(assets.StoreWeaponPool.Weapons.Count);
                while (taken.Contains(roll) && taken.Count < assets.StoreWeaponPool.Weapons.Count)
                    roll = (roll + 1) % assets.StoreWeaponPool.Weapons.Count;
                taken.Add(roll);

                WeaponDataAsset data = assets.Resolve(assets.StoreWeaponPool.Weapons[roll]);
                if (data == null)
                    continue;

                SimWeapon offer = RollOffer(data, survivalTime, assets.LevelUpPerkPool, tuning.GetWeight);
                double price = D(store.WeaponOfferBasePrice) + D(store.WeaponOfferPricePerPerk) * offer.Perks.Count;
                double dps = OfferDps(player, offer);

                if (bought < scenario.WeaponBuysPerBreak && dps >= currentDps * scenario.WeaponBuyThreshold && Spend(player, price))
                {
                    player.Weapon = offer;
                    player.WeaponsBought++;
                    bought++;
                    currentDps = dps;
                    player.PickLog.Add($"Break{breathingIndex}: bought {offer.Name} L{offer.Level} +{offer.Perks.Count}p for {price:0}");
                }
                else
                {
                    player.PickLog.Add($"Break{breathingIndex}: skipped {offer.Name} L{offer.Level} +{offer.Perks.Count}p ({price:0} coins, {dps / Math.Max(1, currentDps):P0} of current DPS, wallet {player.Coins:0})");
                }
            }

            if (store.OfferWeaponLevelUp)
            {
                double price = D(store.WeaponLevelUpBasePrice) + D(store.WeaponLevelUpPricePerLevel) * player.Weapon.Level;
                if (Spend(player, price))
                {
                    player.Weapon.AddLevel(D(store.WeaponLevelUpDamageBonusPerLevel));
                    player.PickLog.Add($"Break{breathingIndex}: weapon level -> {player.Weapon.Level} for {price:0}");
                }
            }
        }

        private void Blacksmith(SimPlayer player, int breathingIndex)
        {
            BlacksmithConfig forge = assets.Blacksmith;
            if (forge == null || assets.BlacksmithPerkPool == null)
                return;

            BlacksmithBreakTuning tuning = forge.ResolveBreakTuning(breathingIndex);
            List<WeaponPerkData> available = PerkCandidates(assets.BlacksmithPerkPool, player).ToList();
            var offers = new List<WeaponPerkData>();

            for (int i = 0; i < forge.PerkChoiceCount && available.Count > 0; i++)
            {
                WeaponPerkData perk = DrawPerk(available, p => tuning.GetWeight(p.Rarity));
                available.Remove(perk);
                offers.Add(perk);
            }

            for (int buy = 0; buy < scenario.BlacksmithBuysPerBreak && offers.Count > 0 && player.Weapon.HasFreeSlot; buy++)
            {
                WeaponPerkData best = null;
                double bestGain = 0;

                foreach (WeaponPerkData perk in offers)
                {
                    double price = D(forge.ResolvePerkPrice(perk.Rarity));
                    if (Spendable(player) < price)
                        continue;

                    SimPlayer preview = player.Clone();
                    preview.Weapon.AddPerk(perk, scenario.UnquantifiedPerkDpsValue);
                    double gain = (preview.TotalDps(scenario) - player.TotalDps(scenario)) / Math.Max(1, price);

                    if (gain > bestGain)
                    {
                        bestGain = gain;
                        best = perk;
                    }
                }

                if (best == null)
                    break;

                double cost = D(forge.ResolvePerkPrice(best.Rarity));
                Spend(player, cost);
                player.Weapon.AddPerk(best, scenario.UnquantifiedPerkDpsValue);
                player.PerksBought++;
                offers.Remove(best);
                player.PickLog.Add($"Break{breathingIndex}: forged {best.name} ({best.Rarity}) for {cost:0}");
            }
        }
    }
}
