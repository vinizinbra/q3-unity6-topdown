namespace QuantumUser.Editor
{
    using Photon.Deterministic;
    using Quantum;
    using QuantumUser.View.Util;
    using UnityEditor;
    using UnityEngine;

    // Fixes a real misfiling bug and authors a placeholder World 1 boss so
    // SurvivalPhase.BossPrototype no longer has to sit empty.
    //
    // ============================== THE BUG ==============================
    // Assets/_QuantumUser/Resources/Enemy/GrasslandOutpostBoss.asset is NOT a Grassland Outpost
    // boss - its internal Quantum Identifier.Guid (464407345595852480) is the exact one
    // Assets/_QuantumUser/Entities/Enemies/ScrapyardJawBoss.prefab's own QPrototypeEnemy.Prototype.
    // EnemyData already references, and its content (EnemyName "ScrapyardJaw", Title "BEHEMOOTH",
    // Subtitle "THE SCRAP GIANT", populated SkillActions/ComboChains) is entirely Scrapjaw's. There
    // is no Grassland Outpost boss data or art anywhere in the project. Step 1 below splits this
    // apart: the misfiled file is MOVED (AssetDatabase.MoveAsset - preserves its GUID/Identifier
    // exactly, so ScrapyardJawBoss.prefab keeps working unchanged) to its real name, freeing the
    // GrasslandOutpostBoss.asset path for an actual Grassland Outpost boss.
    //
    // ============================== THE PLACEHOLDER ==============================
    // No Grassland-Outpost-themed boss art exists yet either, so this is explicitly a PLACEHOLDER,
    // not a finished boss: a genuinely new BossDataAsset (fresh identity, empty Phases/Stagger/
    // ComboChains - confirmed safe, BossSystem.cs's own TickPhase/TickStagger/TickComboChain/
    // TickRetarget each no-op on empty/zero config, so this behaves as a plain single-phase boss
    // driven by the normal EnemySystem AI) reusing HeavySlammer's own "HeavySlamAttack"
    // EnemyActionData as its BasicAction (a real, working, large-telegraphed-AoE attack, matching
    // the "boss = escalated HeavySlammer" language World 1's own Elite roster already establishes -
    // EliteHeavySlammer.asset itself is just HeavySlammer's kit at Elite tier, same reuse
    // precedent), and reusing HeavySlammer's own ViewPrefab (Assets/_Project/Prefabs/View/EnemyView/
    // ScavengerRaider/ScavengerHunt-Slammer.prefab) as stand-in visuals, nested directly under the
    // new EntityPrototype's "Body" child exactly the way ScrapyardJawBoss.prefab already bakes its
    // own rig in (EnemyView.spriteRoot's own documented "one-off prototype can author its own
    // EnemyViewRig as a real child, skipping ViewPrefab entirely" escape hatch) - Stats.Radius is
    // set above a normal enemy's baseline (2 vs the usual 1) so ResolveFitScale still reads it as
    // visibly larger even reusing the same sprite. Reskin with real art/phases/combos later.
    //
    // ============================== WIRING BossPrototype ==============================
    // This script does NOT itself assign the new prefab into any SurvivalConfig's BossPrototype
    // field - Quantum auto-bakes a linked EntityPrototype asset (a `.qprototype` file, e.g.
    // ScrapyardJawBossEntityPrototype.qprototype) alongside a QuantumEntityPrototype-carrying prefab
    // via its own background import pipeline, which does not necessarily finish within this same
    // script execution. SurvivalWorld1ContentGenerator/SurvivalWorld1Iteration2ContentGenerator's
    // own Generate() methods each make a best-effort attempt to load
    // GrasslandOutpostBossEntityPrototype.qprototype and assign it - run THIS generator first, let
    // Unity finish importing, then (re-)run either of those.
    public static class GrasslandOutpostBossGenerator
    {
        private const string EnemyResourcesFolder = "Assets/_QuantumUser/Resources/Enemy";
        private const string EntitiesFolder = "Assets/_QuantumUser/Entities/Enemies";
        private const string MisfiledAssetPath = EnemyResourcesFolder + "/GrasslandOutpostBoss.asset";
        private const string ScrapyardJawBossAssetPath = EnemyResourcesFolder + "/ScrapyardJawBoss.asset";
        private const string ScrapyardJawBossPrefabPath = EntitiesFolder + "/ScrapyardJawBoss.prefab";
        private const string GrasslandBossAssetPath = EnemyResourcesFolder + "/GrasslandOutpostBoss.asset";
        private const string GrasslandBossPrefabPath = EntitiesFolder + "/GrasslandOutpostBoss.prefab";
        private const string HeavySlammerAssetPath = EnemyResourcesFolder + "/BaseEnemies/HeavySlammer.asset";
        private const string HeavySlammerViewPrefabPath = "Assets/_Project/Prefabs/View/EnemyView/ScavengerRaider/ScavengerHunt-Slammer.prefab";

        [MenuItem("Tools/RiftRaiders/Generate Grassland Outpost Boss (Placeholder)")]
        internal static void Generate()
        {
            // ---- Step 1: split the misfiled asset apart ----
            if (AssetDatabase.LoadAssetAtPath<BossDataAsset>(ScrapyardJawBossAssetPath) != null)
            {
                LogHelper.Error("GrasslandOutpostBossGenerator", $"{ScrapyardJawBossAssetPath} already exists - refusing to move {MisfiledAssetPath} over it. Nothing changed.");
                return;
            }

            var misfiledAsset = AssetDatabase.LoadAssetAtPath<BossDataAsset>(MisfiledAssetPath);

            if (misfiledAsset == null)
            {
                LogHelper.Error("GrasslandOutpostBossGenerator", $"No BossDataAsset found at {MisfiledAssetPath} - has this already been split/renamed? Aborting.");
                return;
            }

            string moveError = AssetDatabase.MoveAsset(MisfiledAssetPath, ScrapyardJawBossAssetPath);

            if (string.IsNullOrEmpty(moveError) == false)
            {
                LogHelper.Error("GrasslandOutpostBossGenerator", $"Failed to move {MisfiledAssetPath} -> {ScrapyardJawBossAssetPath}: {moveError}");
                return;
            }

            var movedAsset = AssetDatabase.LoadAssetAtPath<BossDataAsset>(ScrapyardJawBossAssetPath);
            movedAsset.name = "ScrapyardJawBoss";
            EditorUtility.SetDirty(movedAsset);
            AssetDatabase.SaveAssets();

            LogHelper.Log("GrasslandOutpostBossGenerator", $"Moved the misfiled Scrapjaw boss data to {ScrapyardJawBossAssetPath} - {ScrapyardJawBossBossPrefabRefUnchanged()}");

            // ---- Step 2: author the new, genuinely-Grassland-Outpost BossDataAsset ----
            var heavySlammerBasicAction = LoadHeavySlammerBasicActionRef();

            if (heavySlammerBasicAction.Id.IsValid == false)
            {
                LogHelper.Error("GrasslandOutpostBossGenerator", "Couldn't resolve HeavySlammer's own BasicAction (HeavySlamAttack) - the new boss would have no attack. Aborting before creating anything else.");
                return;
            }

            var bossAsset = ScriptableObject.CreateInstance<BossDataAsset>();
            bossAsset.EnemyName = "Rukk Titan";
            bossAsset.Title = "RUKK TITAN";
            bossAsset.Subtitle = "TERROR OF THE OUTPOST";
            bossAsset.Tier = EnemyTier.Boss;
            bossAsset.Stats.Radius = 2;
            bossAsset.Stats.MoveSpeed = FP.FromString("2.5");
            bossAsset.Actions.BasicAction = heavySlammerBasicAction;
            bossAsset.Economy.CostMultiplier = FP._1;
            // Phases/Stagger/ComboChains/GlobalActionSlots/RetargetInterval intentionally left at
            // their default-empty/zero values - see this file's own "THE PLACEHOLDER" note above for
            // why that's a confirmed-safe no-op rather than a broken boss.

            AssetDatabase.CreateAsset(bossAsset, GrasslandBossAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(); // lets QuantumAssetObjectPostprocessor stamp Guid/Identifier on the new asset

            var reloadedBossAsset = AssetDatabase.LoadAssetAtPath<BossDataAsset>(GrasslandBossAssetPath);

            if (reloadedBossAsset == null)
            {
                LogHelper.Error("GrasslandOutpostBossGenerator", $"Failed to (re)load {GrasslandBossAssetPath} right after creating it.");
                return;
            }

            LogHelper.Log("GrasslandOutpostBossGenerator", $"Created {GrasslandBossAssetPath} (EnemyName={reloadedBossAsset.EnemyName}, Tier={reloadedBossAsset.Tier}).");

            // ---- Step 3: duplicate ScrapyardJawBoss.prefab into a real GrasslandOutpostBoss
            // EntityPrototype, then rewire it to the new BossDataAsset + HeavySlammer's rig ----
            if (AssetDatabase.LoadAssetAtPath<GameObject>(ScrapyardJawBossPrefabPath) == null)
            {
                LogHelper.Error("GrasslandOutpostBossGenerator", $"No prefab found at {ScrapyardJawBossPrefabPath} to duplicate - data assets were created/moved above, but no EntityPrototype was authored. Assign one by hand.");
                return;
            }

            if (AssetDatabase.CopyAsset(ScrapyardJawBossPrefabPath, GrasslandBossPrefabPath) == false)
            {
                LogHelper.Error("GrasslandOutpostBossGenerator", $"Failed to copy {ScrapyardJawBossPrefabPath} -> {GrasslandBossPrefabPath}.");
                return;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            GameObject root = PrefabUtility.LoadPrefabContents(GrasslandBossPrefabPath);

            try
            {
                root.name = "GrasslandOutpostBoss";

                var prototypeEnemy = root.GetComponent<QPrototypeEnemy>();

                if (prototypeEnemy == null)
                {
                    LogHelper.Error("GrasslandOutpostBossGenerator", $"{GrasslandBossPrefabPath}'s duplicated root has no QPrototypeEnemy component - can't point it at the new BossDataAsset.");
                    return;
                }

                prototypeEnemy.Prototype.EnemyData = new AssetRef<EnemyDataAsset>(reloadedBossAsset.Guid);

                Transform body = root.transform.Find("Body");

                if (body == null)
                {
                    LogHelper.Error("GrasslandOutpostBossGenerator", $"{GrasslandBossPrefabPath}'s duplicated root has no 'Body' child (EnemyView.spriteRoot's expected target) - visuals not rewired.");
                }
                else
                {
                    // Remove the duplicated Scrapjaw visual (ScavengerHunt-Boss) and nest HeavySlammer's
                    // own ViewPrefab in its place instead - see this file's own "THE PLACEHOLDER" note.
                    for (int i = body.childCount - 1; i >= 0; i--)
                    {
                        Object.DestroyImmediate(body.GetChild(i).gameObject);
                    }

                    var heavySlammerViewPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HeavySlammerViewPrefabPath);

                    if (heavySlammerViewPrefab == null)
                    {
                        LogHelper.Error("GrasslandOutpostBossGenerator", $"No prefab found at {HeavySlammerViewPrefabPath} - Body left with no visual child.");
                    }
                    else
                    {
                        var viewInstance = (GameObject)PrefabUtility.InstantiatePrefab(heavySlammerViewPrefab, body);
                        viewInstance.name = "GrasslandOutpostBossView (HeavySlammer placeholder rig)";
                        viewInstance.transform.localPosition = Vector3.zero;
                        viewInstance.transform.localRotation = Quaternion.identity;
                        viewInstance.transform.localScale = Vector3.one;
                    }
                }

                PrefabUtility.SaveAsPrefabAsset(root, GrasslandBossPrefabPath);
                LogHelper.Log("GrasslandOutpostBossGenerator", $"Created {GrasslandBossPrefabPath} - a placeholder Grassland Outpost boss EntityPrototype (HeavySlammer's rig/attack reused as stand-in art/kit). Let Unity finish importing, then re-run either SurvivalWorld1ContentGenerator or SurvivalWorld1Iteration2ContentGenerator to pick up GrasslandOutpostBossEntityPrototype.qprototype into BossPrototype.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static string ScrapyardJawBossBossPrefabRefUnchanged() =>
            "ScrapyardJawBoss.prefab's own QPrototypeEnemy.Prototype.EnemyData reference is unaffected (AssetDatabase.MoveAsset preserves the file's Quantum Identifier/GUID, only its path/name changed).";

        // HeavySlammer's BasicAction is an EMBEDDED sub-asset inside HeavySlammer.asset itself (Quantum's
        // [ExpandableAsset] convention - not a separate .asset file), so it has to be resolved via
        // AssetDatabase.LoadAllAssetsAtPath rather than LoadAssetAtPath<EnemyActionData>.
        private static AssetRef<EnemyActionData> LoadHeavySlammerBasicActionRef()
        {
            foreach (Object obj in AssetDatabase.LoadAllAssetsAtPath(HeavySlammerAssetPath))
            {
                if (obj is EnemyActionData actionData)
                {
                    return new AssetRef<EnemyActionData>(actionData.Guid);
                }
            }

            LogHelper.Error("GrasslandOutpostBossGenerator", $"No embedded EnemyActionData sub-asset found inside {HeavySlammerAssetPath}.");
            return default;
        }
    }
}
