namespace QuantumUser.Editor
{
    using Quantum;
    using UnityEditor;
    using UnityEngine;

    // One-time bridge for the EnemyDataAsset field reorg (grouping flat fields into
    // Economy/Stats/AI/Knockback/Actions sub-structs - Stats folds together the old flat
    // MoveSpeed/Radius/MaxHealth/Height/Movement plus the old separate Shield/Defense groups,
    // since together they're all just "how tough/fast/mobile is this enemy"). Run this once,
    // verify the 7 existing EnemyDataAsset .asset instances still show the same values under their
    // new grouped fields, then delete this file AND the "MIGRATION BRIDGE" field block at the
    // bottom of EnemyDataAsset.cs.
    public static class EnemyDataAssetMigration
    {
        [MenuItem("Tools/Quantum/Migrate EnemyDataAsset Fields")]
        private static void Migrate()
        {
            var migrated = 0;

            foreach (var guid in AssetDatabase.FindAssets("t:EnemyDataAsset"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<EnemyDataAsset>(path);
                if (asset == null) continue;

                asset.Economy = new EnemyEconomyData
                {
                    Persistent = asset.Persistent,
                    SpawnProfile = asset.SpawnProfile,
                };

                asset.Stats = new EnemyStatsData
                {
                    MoveSpeed = asset.MoveSpeed,
                    Radius = asset.Radius,
                    MaxShield = asset.MaxShield,
                    ShieldRechargeDelay = asset.ShieldRechargeDelay,
                    ShieldRechargeRate = asset.ShieldRechargeRate,
                    Traits = asset.Traits,
                    FrontalDamageReductionAmount = asset.FrontalDamageReductionAmount,
                    FrontalDamageReductionArcDegrees = asset.FrontalDamageReductionArcDegrees,
                    Height = asset.Height,
                    Movement = asset.Movement,
                };

                asset.AI = new EnemyAIData
                {
                    Targeting = asset.Targeting,
                    DetectionRange = asset.DetectionRange,
                    LeashRange = asset.LeashRange,
                };

                asset.Knockback = new EnemyKnockbackData
                {
                    CanBeInterruptedByKnockback = asset.CanBeInterruptedByKnockback,
                    KnockbackRecoveryTime = asset.KnockbackRecoveryTime,
                };

                asset.Actions = new EnemyActionsData
                {
                    BasicAction = asset.BasicAction,
                    SkillActions = asset.SkillActions,
                };

                EditorUtility.SetDirty(asset);
                migrated++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"EnemyDataAssetMigration: migrated {migrated} EnemyDataAsset instance(s). " +
                      "Spot-check their Inspectors, then delete EnemyDataAssetMigration.cs and the " +
                      "MIGRATION BRIDGE field block in EnemyDataAsset.cs.");
        }
    }
}
