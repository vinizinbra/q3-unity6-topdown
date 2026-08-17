using System;
using Quantum;
using QuantumUser.View.Util;
using UnityEngine;

namespace QuantumUser.View
{
    // Quantum-aware glue for DebugUpgradeMenuWindow - builds its button list from live sim state
    // once the local player is set up (MyLocalPlayer.AddOnLocalPlayerSetup fires immediately if
    // already set, or the next time it is - same idiom the couch-coop HUD widgets use in Start()).
    // Unlike the 4 existing <Kind>DebugTrigger classes (one hardcoded AssetRef field each, driven by
    // a Simulation-side Inspector button via a static event), this enumerates every upgrade the
    // local player's own hero can currently reach and sends the exact same commands directly - it
    // already lives in the View layer, so it doesn't need that Simulation-can't-reach-QuantumRunner
    // indirection.
    public class DebugUpgradeMenuTrigger : QuantumGlobalMonoBehaviour
    {
        [SerializeField] private DebugUpgradeMenuWindow menu;

        private void Start()
        {
            if (MyLocalPlayer.Instance != null)
                MyLocalPlayer.Instance.AddOnLocalPlayerSetup(_ => Rebuild());
        }

        private void Rebuild()
        {
            if (MyLocalPlayer.Instance == null || MyLocalPlayer.Instance.IsLocalPlayerSetup == false)
            {
                LogHelper.Warn("DebugUpgradeMenuTrigger", "no local player set up yet");
                return;
            }

            Frame frame = _game.Frames.Predicted;
            EntityRef entity = MyLocalPlayer.Instance.EntityRef;

            menu.Clear();

            CharacterStats stats = frame.Get<CharacterStats>(entity);
            CharacterData hero = frame.FindAsset(stats.CharacterData);
            CharacterSkills skills = frame.Get<CharacterSkills>(entity);

            menu.AddLabel(menu.HeroContent, "Dash");
            foreach (AssetRef<SkillActionData> upgrade in hero.DashSkillUpgrades)
                AddSkillUpgradeButton(frame, entity, upgrade, SkillSlotId.DashSkill, "Dash Skill", skills.DashSkill);

            menu.AddLabel(menu.HeroContent, "Hero Skill");
            if (hero.HeroSkill.IsValid == true)
            {
                // No separate HeroSkillUpgrades list - pulled straight from HeroSkill's own Actions.
                // LevelUpUtility.AddHeroSkillUpgradeCandidates only offers the Activated == false
                // subset (an Activated == true entry is already running for everyone, so a real
                // level-up screen has nothing left to grant there) - this debug menu deliberately
                // lists every entry regardless of Activated, for full visibility/control while
                // testing, even though granting an already-Activated one has no visible effect.
                SkillData heroSkillData = frame.FindAsset(hero.HeroSkill);
                foreach (AssetRef<SkillActionData> upgrade in heroSkillData.Actions)
                {
                    if (upgrade.IsValid == false)
                        continue;

                    AddSkillUpgradeButton(frame, entity, upgrade, SkillSlotId.HeroSkill, "Hero Skill", skills.HeroSkill);
                }
            }

            menu.AddLabel(menu.HeroContent, "Passive");
            foreach (AssetRef<PassiveUpgradeData> upgrade in hero.PassiveUpgrades)
            {
                PassiveUpgradeData data = frame.FindAsset(upgrade);
                int currentStacks = 0;
                int maxStacks = 0;
                bool granted = false;
                string description = data.GetDescription();
                Func<int, string> descriptionForRank = null;

                // Ranked Ascension (MaxRank > 1, see docs/level-up-upgrades.md "Ranked Ascensions") -
                // same current/max readout the real level-up card shows, "granted" means fully
                // ranked up (nothing left to add), description previews the NEXT rank.
                // descriptionForRank lets the row advance its own preview text locally on repeat
                // clicks (see DebugUpgradeButtonWidget.Setup) - GetDescription(int) is a pure function
                // of the asset, no sim round trip needed. A non-ranked Passive Upgrade keeps its
                // original always-ungranted/no-stacks behavior - it has no granted-tracking to read
                // at all (see this file's own header comment).
                if (data.MaxRank > 1)
                {
                    currentStacks = PassiveUpgradeUtility.GetRank(frame, entity, upgrade);
                    maxStacks = data.MaxRank;
                    granted = PassiveUpgradeUtility.IsAlreadyPicked(frame, entity, upgrade);
                    description = data.GetDescription(currentStacks + 1);
                    descriptionForRank = data.GetDescription;
                }

                menu.AddButton(menu.HeroContent, "Passive", data.DisplayName, data.Icon, description, granted,
                    () => SendGrantPassive(upgrade), null, currentStacks, maxStacks, descriptionForRank);
            }

            if (frame.RuntimeConfig.LevelUpConfig.IsValid == false)
            {
                LogHelper.Warn("DebugUpgradeMenuTrigger", "RuntimeConfig has no LevelUpConfig assigned - Weapon Perk/Global rows skipped");
                return;
            }

            LevelUpConfig config = frame.FindAsset(frame.RuntimeConfig.LevelUpConfig);

            if (config.WeaponPerkPool.IsValid)
            {
                WeaponPerkPoolData pool = frame.FindAsset(config.WeaponPerkPool);
                Weapon weapon = frame.Get<Weapon>(entity);

                foreach (AssetRef<WeaponPerkData> perk in pool.Perks)
                {
                    WeaponPerkData data = frame.FindAsset(perk);
                    bool granted = ContainsUpgrade(weapon.Perks, perk);
                    menu.AddButton(menu.WeaponPerkContent, "Weapon Perk", data.DisplayName, data.Icon, data.GetDescription(), granted,
                        () => SendGrantPerk(perk), null);
                }
            }

            foreach (AssetRef<GlobalUpgradeData> upgrade in config.GlobalUpgrades)
            {
                GlobalUpgradeData data = frame.FindAsset(upgrade);

                // Only a capped upgrade (MaxPicks > 0) has any pick history to read at all - see
                // GlobalUpgradeUtility.GetPickCount/GlobalUpgradeData.MaxPicks, same cap
                // LevelUpUtility.IsCappedOut enforces on the real level-up screen. "granted" here
                // means fully maxed out (nothing left to add), same meaning DebugUpgradeButtonWidget
                // gives it for the other kinds - a partially-stacked capped upgrade still shows its
                // "Add" button, just with the current/max readout alongside it.
                int currentStacks = 0;
                int maxStacks = data.MaxPicks;
                bool maxedOut = false;

                if (maxStacks > 0)
                {
                    currentStacks = GlobalUpgradeUtility.GetPickCount(frame, entity, upgrade);
                    maxedOut = currentStacks >= maxStacks;
                }

                menu.AddButton(menu.GlobalContent, "Global", data.DisplayName, data.Icon, data.GetDescription(), maxedOut,
                    () => SendGrantGlobal(upgrade), null, currentStacks, maxStacks);
            }

            foreach (AssetRef<RiftMutationData> mutation in config.RiftMutations)
            {
                RiftMutationData data = frame.FindAsset(mutation);

                // Every Rift Mutation caps at 1 pick pool-wide (see RiftMutationData/
                // RiftMutationPicks) - no stacks readout needed, "granted" just means already
                // picked, same meaning IsAlreadyPicked gives LevelUpUtility on a real roll.
                bool granted = RiftMutationUtility.IsAlreadyPicked(frame, entity, mutation);

                menu.AddButton(menu.RiftContent, "Rift Mutation", data.DisplayName, data.Icon, data.GetDescription(), granted,
                    () => SendGrantRift(mutation), null);
            }

            foreach (AssetRef<RiftMutationData> mutation in config.RiftMarkMutations)
            {
                RiftMutationData data = frame.FindAsset(mutation);

                // Same shared RiftMutationPicks/IsAlreadyPicked as the core Rift Mutation pool above
                // - RiftMarkMutations draws from the same RiftMutationData catalog, just a different
                // list/LevelUpPoolKind.
                bool granted = RiftMutationUtility.IsAlreadyPicked(frame, entity, mutation);

                menu.AddButton(menu.RiftMarkContent, "Rift Mark Mutation", data.DisplayName, data.Icon, data.GetDescription(), granted,
                    () => SendGrantRift(mutation), null);
            }
        }

        private void AddSkillUpgradeButton(Frame frame, EntityRef entity, AssetRef<SkillActionData> upgrade, SkillSlotId slot, string category, SkillSlot ownerSlot)
        {
            SkillActionData data = frame.FindAsset(upgrade);
            int currentStacks = 0;
            int maxStacks = 0;
            bool granted;
            string description;
            System.Action onDeactivate;
            Func<int, string> descriptionForRank = null;

            // Ranked Ascension (MaxRank > 1, see docs/level-up-upgrades.md "Ranked Ascensions") - same
            // current/max readout the real level-up card shows, "granted" means fully ranked up,
            // description previews the NEXT rank. descriptionForRank lets the row advance its own
            // preview text locally on repeat clicks (see DebugUpgradeButtonWidget.Setup) -
            // GetDescription(int) is a pure function of the asset, no sim round trip needed. No
            // "Remove" for a ranked action - SkillSystem.RemoveUpgrade only clears the slot entry, it
            // doesn't decrement UpgradeHistory.Count, so removing then re-adding would desync the rank
            // readout from what's actually equipped; same "no real revert path" treatment Weapon
            // Perk/Passive/Global already get.
            if (data.MaxRank > 1)
            {
                currentStacks = SkillUpgradeUtility.GetRank(frame, entity, upgrade);
                maxStacks = data.MaxRank;
                granted = SkillUpgradeUtility.IsCappedOut(frame, entity, upgrade);
                description = data.GetDescription(currentStacks + 1);
                descriptionForRank = data.GetDescription;
                onDeactivate = null;
            }
            else
            {
                granted = ContainsUpgrade(ownerSlot.Upgrades, upgrade);
                description = data.GetDescription();
                onDeactivate = () => SendRemoveSkill(upgrade, slot);
            }

            menu.AddButton(menu.HeroContent, category, data.DisplayName, data.Icon, description, granted,
                () => SendGrantSkill(upgrade, slot), onDeactivate, currentStacks, maxStacks, descriptionForRank);
        }

        // Same lookup SkillSystem.AddUpgrade itself does before granting - see docs/level-up-upgrades.md
        // "Why exclude already-granted skill upgrades". Reused here purely to mark a row's granted
        // state, not to block a re-click.
        private static bool ContainsUpgrade<T>(FixedArray<AssetRef<T>> upgrades, AssetRef<T> upgrade) where T : AssetObject
        {
            for (int i = 0; i < upgrades.Length; i++)
                if (upgrades[i] == upgrade)
                    return true;

            return false;
        }

        // Each row's own button deactivates itself the instant it's clicked (see
        // DebugUpgradeButtonWidget.Setup) rather than this class polling the sim to learn the new
        // granted state back - so these just fire the command and stop, same as the existing
        // <Kind>DebugTrigger classes do.
        private void SendGrantSkill(AssetRef<SkillActionData> upgrade, SkillSlotId slot)
        {
            _game.SendCommand(new GrantSkillUpgradeCommand { Upgrade = upgrade, Slot = slot });
        }

        private void SendRemoveSkill(AssetRef<SkillActionData> upgrade, SkillSlotId slot)
        {
            _game.SendCommand(new RemoveSkillUpgradeCommand { Upgrade = upgrade, Slot = slot });
        }

        private void SendGrantPassive(AssetRef<PassiveUpgradeData> upgrade)
        {
            _game.SendCommand(new GrantPassiveUpgradeCommand { Upgrade = upgrade });
        }

        private void SendGrantPerk(AssetRef<WeaponPerkData> perk)
        {
            _game.SendCommand(new GrantWeaponPerkCommand { Perk = perk });
        }

        private void SendGrantGlobal(AssetRef<GlobalUpgradeData> upgrade)
        {
            _game.SendCommand(new GrantGlobalUpgradeCommand { Upgrade = upgrade });
        }

        private void SendGrantRift(AssetRef<RiftMutationData> mutation)
        {
            _game.SendCommand(new GrantRiftMutationCommand { Mutation = mutation });
        }

        public override void QStart(QuantumGame game)
        {
        }
        public override void QUpdate(QuantumGame game) { }
        public override void QLateUpdate(QuantumGame game) { }
    }
}
