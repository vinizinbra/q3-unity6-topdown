namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Applies one CheatCommand per sending player per tick (see CheatCommand for why this handler
    // compiles on every client, not just cheat-enabled builds). Registered OUTSIDE
    // GameplaySystemGroup in SystemSetup.User.cs, next to DebugCheatSystem, so Continue/AdvancePhase
    // still fire while the gameplay group is paused - otherwise a Pause could never be undone.
    //
    // The phase-advance cheats deliberately only move CurrentPhaseIndex (+ reset PhaseTimer/
    // PhaseGuaranteedSpawnDone), exactly like SurvivalProgressionUtility's own natural advance -
    // CombatDirectorSystem.ApplyPhaseGameState then runs every real transition side effect
    // (BreathingIndex++, BeginBossEncounter, POI sweeps) off that changed index next tick, so there
    // is no transition logic duplicated here to keep in sync.
    [Preserve]
    public unsafe class CheatSystem : SystemMainThreadFilter<CheatSystem.Filter>
    {
        public struct Filter
        {
            public EntityRef Entity;
            public PlayerLink* PlayerLink;
        }

        public override void Update(Frame f, ref Filter filter)
        {
            if (f.GetPlayerCommand(filter.PlayerLink->Player) is not CheatCommand cmd)
                return;

            Log.Debug($"[Cheat] {filter.Entity} fired {cmd.Action}");
            Apply(f, filter.Entity, cmd);
        }

        private static void Apply(Frame f, EntityRef player, CheatCommand cmd)
        {
            switch (cmd.Action)
            {
                case CheatActionKind.Pause:
                    f.SystemDisable<GameplaySystemGroup>();
                    break;

                case CheatActionKind.Continue:
                    f.SystemEnable<GameplaySystemGroup>();
                    break;

                case CheatActionKind.Advance1Min:
                    f.Global->SurvivalTime += (FP)60;
                    break;

                case CheatActionKind.AdvancePhase:
                    AdvancePhase(f);
                    break;

                case CheatActionKind.AdvanceToNextBreathing:
                    AdvanceToNextBreathing(f);
                    break;

                case CheatActionKind.LevelUp:
                    LevelUpOnce(f);
                    break;

                case CheatActionKind.GetWeapon:
                    GrantWeapon(f, player, new AssetRef<WeaponDataAsset>(new AssetGuid(cmd.AssetId)));
                    break;

                case CheatActionKind.GetRiftMutation:
                    RiftMutationUtility.Grant(f, player, new AssetRef<RiftMutationData>(new AssetGuid(cmd.AssetId)));
                    break;

                case CheatActionKind.GrantGlobalUpgrade:
                    GlobalUpgradeUtility.Grant(f, player, new AssetRef<GlobalUpgradeData>(new AssetGuid(cmd.AssetId)));
                    break;

                case CheatActionKind.BuyAccessory:
                    AccessoryGuardUtility.Restore(f, player);
                    break;

                case CheatActionKind.GrantCoins:
                    CoinUtility.Grant(f, player, (FP)cmd.Amount);
                    break;

                case CheatActionKind.ToggleGodMode:
                    if (f.Has<Invulnerable>(player))
                        f.Remove<Invulnerable>(player);
                    else
                        f.Add<Invulnerable>(player);
                    break;

                case CheatActionKind.KillAllEnemies:
                    KillAllEnemies(f, player);
                    break;

                case CheatActionKind.HealFull:
                    if (f.Unsafe.TryGetPointer<Health>(player, out var health))
                        health->CurrentHealth = health->MaxHealth;
                    break;

                case CheatActionKind.OpenChest:
                    LevelUpUtility.BeginChestScreen(f, player, LevelUpCategory.GlobalUpgrade);
                    break;

                case CheatActionKind.Revive:
                    PlayerLifeStateUtility.ReviveAllIncapacitated(f);
                    break;
            }
        }

        private static void AdvancePhase(Frame f)
        {
            SurvivalConfig config = f.FindAsset(f.RuntimeConfig.SurvivalConfig);
            if (config == null || config.Phases == null)
                return;

            if (f.Global->CurrentPhaseIndex >= config.Phases.Length - 1)
                return;

            f.Global->CurrentPhaseIndex++;
            f.Global->PhaseTimer = FP._0;
            f.Global->PhaseGuaranteedSpawnDone = false;
        }

        private static void AdvanceToNextBreathing(Frame f)
        {
            SurvivalConfig config = f.FindAsset(f.RuntimeConfig.SurvivalConfig);
            if (config == null || config.Phases == null)
                return;

            int index = f.Global->CurrentPhaseIndex;

            // Walk forward to the first Breathing phase strictly after the current one, stopping at
            // the last phase either way.
            while (index < config.Phases.Length - 1)
            {
                index++;
                if (config.Phases[index].Kind == SurvivalPhaseKind.Breathing)
                    break;
            }

            if (index == f.Global->CurrentPhaseIndex)
                return;

            f.Global->CurrentPhaseIndex = index;
            f.Global->PhaseTimer = FP._0;
            f.Global->PhaseGuaranteedSpawnDone = false;

            // Move the run clock forward to match the jump, so difficulty curves and the HUD survival
            // timer line up with the target phase. SurvivalTime is the total combat time that
            // precedes this phase - Breathing phases never advance it, so they're excluded from the
            // sum. Guarded so it only ever moves forward, never rewinds a clock already past it.
            FP survivedByTarget = FP._0;
            for (int i = 0; i < index; i++)
            {
                if (config.Phases[i].Kind != SurvivalPhaseKind.Breathing)
                    survivedByTarget += config.Phases[i].Duration;
            }

            if (f.Global->SurvivalTime < survivedByTarget)
                f.Global->SurvivalTime = survivedByTarget;
        }

        private static void LevelUpOnce(Frame f)
        {
            if (f.RuntimeConfig.ExperienceConfig.IsValid == false)
                return;

            ExperienceConfig config = f.FindAsset(f.RuntimeConfig.ExperienceConfig);
            FP multiplier = ExperienceUtility.ResolveXpRequirementMultiplier(f);

            // Grant exactly enough to reach the next display level (Level + 2, see ExperienceUtility)
            // - a single level, not a fixed lump that could stack several upgrade screens at once.
            FP needed = ExperienceUtility.GetRequiredExperience(config, f.Global->Level + 2, multiplier);
            FP delta = needed - f.Global->TotalExperience;
            if (delta < FP._0)
                delta = FP._0;

            ExperienceUtility.Grant(f, delta + FP._1);
        }

        private static void GrantWeapon(Frame f, EntityRef player, AssetRef<WeaponDataAsset> weaponData)
        {
            if (f.Unsafe.TryGetPointer<Weapon>(player, out var weapon) == false)
                return;

            WeaponSystem.Equip(f, player, weapon, weaponData);
        }

        private static void KillAllEnemies(Frame f, EntityRef killer)
        {
            // Collected first, then killed - ApplyDamage can destroy the entity, which must not
            // happen mid-filter-iteration.
            List<EntityRef> enemies = new List<EntityRef>();
            var filtered = f.Filter<Enemy>();
            while (filtered.Next(out EntityRef entity, out Enemy _))
                enemies.Add(entity);

            foreach (EntityRef enemy in enemies)
            {
                if (f.Unsafe.TryGetPointer<Health>(enemy, out var health) && health->CurrentHealth > FP._0)
                    DamageUtility.ApplyDamage(f, enemy, health->CurrentHealth, killer);
            }
        }
    }
}
