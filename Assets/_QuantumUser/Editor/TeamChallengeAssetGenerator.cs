namespace QuantumUser.Editor
{
    using System.Collections.Generic;
    using System.Linq;
    using Photon.Deterministic;
    using Quantum;
    using QuantumUser.View.Util;
    using UnityEditor;
    using UnityEngine;

    // Scaffolds the DATA side of Optional Team Challenge (see docs/optional-team-challenge.md) -
    // one ChallengeDefinition.asset per initial ChallengeType, plus one TeamChallengeConfig.asset
    // wiring all three into its ChallengePool. Mirrors RiftMutationAssetGenerator.cs exactly (same
    // folder-creation/update-in-place/second-pass-after-refresh shape - the create-or-update
    // pattern makes re-running this safe). Deliberately does NOT touch the POI prefab/EntityPrototype
    // itself - that's authored by hand per POI instance (see the "Editor authoring needed" list in
    // docs/optional-team-challenge.md), same as every other POI in this project.
    //
    // AllowedGroups/AllowedEnemies are deliberately left EMPTY on every generated
    // ChallengeDefinition - this tool has no safe way to guess which of the project's existing
    // EnemyGroupConfig/EnemySpawnEntry assets belong in a given challenge's encounter, and spawning
    // nothing (logged loud, see CombatDirectorUtility.TrySelectSpawn's own "no AllowedGroups
    // authored" check) is a safer default than guessing wrong. Assign at least one by hand after
    // generating - BudgetPerPulse/PulseInterval/TargetPressure/MaxAliveEnemies are seeded with
    // decisive placeholders so the challenge's own CombatDirectorUtility.TryPulse pacing (see
    // TeamChallengeUtility.PulseChallengeEncounter) has something reasonable to start from.
    //
    // Rules (see ChallengeDefinition.View.cs) gets its Text seeded per row too, same decisive-
    // placeholder treatment - only each row's Icon is left null (and preserved across re-runs, see
    // BuildRuleEntries), same "can't guess a sprite asset" reasoning as AllowedGroups above.
    public static class TeamChallengeAssetGenerator
    {
        private const string FolderPath = "Assets/_QuantumUser/Resources/Poi/TeamChallenge";
        private const string ConfigAssetPath = FolderPath + "/TeamChallengeConfig.asset";

        private class DefinitionSpec
        {
            public string FileName;
            public ChallengeType Type;
            public string DisplayName;
            public string Description;
            public FP Duration;
            public int KillTarget;

            // Same fields SurvivalPhase itself carries for CombatDirectorUtility.TryPulse - see
            // ChallengeDefinition's own header comment. Decisive placeholders, not final balance.
            public FP BudgetPerPulse;
            public FP PulseInterval;
            public FP TargetPressure;
            public int MaxAliveEnemies;

            // Rules row TEXT only - see ChallengeDefinition.Rules's own comment. Icon is left null
            // per row, same "can't guess which asset belongs here" reasoning AllowedGroups/
            // AllowedEnemies already document below - assign an icon per row by hand.
            public string[] Rules;
        }

        // Decisive placeholder values, not final balance - a party can iterate these directly in
        // the Inspector once real playtesting starts (same "give decisive numbers, don't over-index
        // on one-shot edge-case math" convention every other first-pass balance pass in this project
        // follows).
        private static readonly List<DefinitionSpec> Specs = new()
        {
            new DefinitionSpec
            {
                FileName = "KillRush", Type = ChallengeType.KillRush, DisplayName = "KILL RUSH",
                Description = "Kill 20 enemies before the timer runs out.",
                Duration = 30, KillTarget = 20,
                BudgetPerPulse = 60, PulseInterval = 3, TargetPressure = 40, MaxAliveEnemies = 15,
                Rules = new[] { "Kill 20 enemies", "Before the 30s timer runs out" }
            },
            new DefinitionSpec
            {
                // No Duration - Flawless Hunt has no timer by default (see ChallengeDefinition.
                // Duration's own comment); it fails immediately on real HP loss instead.
                FileName = "FlawlessHunt", Type = ChallengeType.FlawlessHunt, DisplayName = "FLAWLESS HUNT",
                Description = "Kill 15 enemies without any Raider losing HP.",
                Duration = FP._0, KillTarget = 15,
                BudgetPerPulse = 40, PulseInterval = 4, TargetPressure = 25, MaxAliveEnemies = 10,
                Rules = new[] { "Kill 15 enemies", "No Raider can lose any HP" }
            },
            new DefinitionSpec
            {
                // KillTarget is unused for Cursed Survival - left at 0.
                FileName = "CursedSurvival", Type = ChallengeType.CursedSurvival, DisplayName = "CURSED SURVIVAL",
                Description = "Everyone is pinned to 1 HP with no Accessory - survive 20 seconds.",
                Duration = 20, KillTarget = 0,
                BudgetPerPulse = 50, PulseInterval = 3, TargetPressure = 30, MaxAliveEnemies = 12,
                Rules = new[] { "Every Raider pinned to 1 HP", "Accessory disabled", "Survive 20 seconds" }
            },
        };

        [MenuItem("Tools/RiftRaiders/Poi/Generate Team Challenge Assets")]
        internal static void Generate()
        {
            if (AssetDatabase.IsValidFolder(FolderPath) == false)
            {
                CreateFolderRecursive(FolderPath);
            }

            int created = 0;
            int updated = 0;

            foreach (var spec in Specs)
            {
                string path = $"{FolderPath}/{spec.FileName}.asset";
                var existing = AssetDatabase.LoadAssetAtPath<ChallengeDefinition>(path);
                bool isNew = existing == null;

                ChallengeDefinition asset = isNew ? ScriptableObject.CreateInstance<ChallengeDefinition>() : existing;

                asset.Type = spec.Type;
                asset.DisplayName = spec.DisplayName;
                asset.Description = spec.Description;
                asset.Duration = spec.Duration;
                asset.KillTarget = spec.KillTarget;
                asset.BudgetPerPulse = spec.BudgetPerPulse;
                asset.PulseInterval = spec.PulseInterval;
                asset.TargetPressure = spec.TargetPressure;
                asset.MaxAliveEnemies = spec.MaxAliveEnemies;
                // AllowedGroups/AllowedEnemies deliberately untouched on an existing asset (never
                // cleared/rebuilt) - preserves whatever the user already assigned by hand on a
                // re-run. Only defaulted to empty the first time this asset is ever created.
                asset.AllowedGroups ??= new System.Collections.Generic.List<AssetRef<EnemyGroupConfig>>();
                asset.AllowedEnemies ??= System.Array.Empty<EnemySpawnEntry>();

                // Rules TEXT seeded fresh every run (decisive placeholders, cheap to regenerate) -
                // Icon is preserved on an existing asset instead of being wiped, since that's the
                // one per-row field only a human can assign (see DefinitionSpec.Rules's own comment).
                asset.Rules = BuildRuleEntries(spec.Rules, asset.Rules);

                if (isNew)
                {
                    AssetDatabase.CreateAsset(asset, path);
                    created++;
                }
                else
                {
                    EditorUtility.SetDirty(asset);
                    updated++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(); // lets QuantumAssetObjectPostprocessor stamp Guid/Identifier on anything just created

            GenerateConfig();

            LogHelper.Log("TeamChallengeAssetGenerator",
                $"{created} ChallengeDefinition created, {updated} updated. AllowedGroups/AllowedEnemies were left empty on every newly-created asset - assign at least one before this challenge can spawn anything.");
        }

        // Separate pass, after Refresh - a freshly-created ChallengeDefinition has no Guid stamped
        // until QuantumAssetObjectPostprocessor has run, so resolving these AssetRefs inline with
        // the first pass would silently write empty refs into ChallengePool on a clean generate.
        private static void GenerateConfig()
        {
            var existing = AssetDatabase.LoadAssetAtPath<TeamChallengeConfig>(ConfigAssetPath);
            bool isNew = existing == null;

            TeamChallengeConfig config = isNew ? ScriptableObject.CreateInstance<TeamChallengeConfig>() : existing;

            config.ChallengePool = Specs
                .Select(spec => AssetDatabase.LoadAssetAtPath<ChallengeDefinition>($"{FolderPath}/{spec.FileName}.asset"))
                .Where(asset => asset != null)
                .Select(asset => new AssetRef<ChallengeDefinition>(asset.Guid))
                .ToArray();

            // Placeholder - the spec's own suggested starting point (~2.5x the POI's own small
            // Interaction Area radius). Re-author per POI instance once that radius is authored.
            if (isNew)
            {
                config.ReadyCancelRadius = FP.FromString("7.5");
                config.CountdownDuration = 3;
            }

            if (isNew)
            {
                AssetDatabase.CreateAsset(config, ConfigAssetPath);
            }
            else
            {
                EditorUtility.SetDirty(config);
            }

            AssetDatabase.SaveAssets();

            LogHelper.Log("TeamChallengeAssetGenerator", $"TeamChallengeConfig at {ConfigAssetPath} wired with {config.ChallengePool.Length} ChallengeDefinition(s).");
        }

        // Rebuilds Text for every row from the spec (cheap, always current), but keeps whatever
        // Icon an existing row at the same index already had by hand - a re-run of this generator
        // must never silently wipe icon assignments a designer already made.
        private static ChallengeRuleEntry[] BuildRuleEntries(string[] specRules, ChallengeRuleEntry[] existing)
        {
            if (specRules == null)
                return System.Array.Empty<ChallengeRuleEntry>();

            var result = new ChallengeRuleEntry[specRules.Length];

            for (int i = 0; i < specRules.Length; i++)
            {
                Sprite icon = existing != null && i < existing.Length ? existing[i].Icon : null;
                result[i] = new ChallengeRuleEntry { Icon = icon, Text = specRules[i] };
            }

            return result;
        }

        private static void CreateFolderRecursive(string folderPath)
        {
            string[] parts = folderPath.Split('/');
            string current = parts[0];

            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";

                if (AssetDatabase.IsValidFolder(next) == false)
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }
    }
}
