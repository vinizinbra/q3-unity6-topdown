using System;
using System.Collections.Generic;
using System.Linq;
using Quantum;
using QuantumUser.View.Util;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;
using Object = UnityEngine.Object;

namespace Project.EditorTools.SpriteOptimizer
{
    internal enum EnemySheetPlacement
    {
        /// <summary>Only one world's enemies draw it.</summary>
        World,
        /// <summary>Several worlds draw it: it lives in the first one (its home); the others borrow it.</summary>
        BorrowedByOtherWorlds,
        /// <summary>Heroes / shared configs / chunks / gameplay scenes (HUD portraits…) draw it too: left in place.</summary>
        Shared,
        /// <summary>No world and no shared content draws it: parked, never packed.</summary>
        Unused,
        /// <summary>Art of a world that isn't in the catalog yet (biome folder atlas): left where it is.</summary>
        Biome,
    }

    internal sealed class EnemySheetPlan
    {
        public string Path;
        public string Destination;
        public EnemySheetPlacement Placement;
        public WorldDefinition Home;
        public readonly List<WorldDefinition> Worlds = new List<WorldDefinition>();
        public string Reason;

        public bool Moves => Destination != null && !SpriteOptimizerSettings.IsUnder(Path, Destination);
        public string Name => System.IO.Path.GetFileName(Path);
    }

    /// <summary>
    /// Puts every enemy sprite sheet in its world's folder, from what each world's enemies actually draw
    /// (<see cref="WorldReachIndex"/>: WorldCatalog → SurvivalConfig → enemies / bosses → actions / deliveries → flattened
    /// view prefabs), then makes each world atlas pack its folder. Where a file lives then decides its atlas
    /// (<see cref="GameplayAtlasRouter"/>), so the Common atlas holds no monsters.
    /// </summary>
    internal static class EnemySpriteOrganizer
    {
        public const string Root = "Assets/_Project/Art/Enemy";
        public const string UnusedFolder = Root + "/_Unused";

        /// <summary>Folders holding enemy art today; every sprite texture under them is planned.</summary>
        public static readonly string[] EnemyArtRoots = { Root, "Assets/_Project/Art/Sprites/Enemy" };

        private const string AtlasFolder = "Assets/_Project/Art/SpriteAtlases";

        public static string WorldFolder(WorldReachIndex reach, WorldDefinition world) => $"{Root}/World{reach.Worlds.IndexOf(world) + 1}";

        public static List<EnemySheetPlan> Analyze(SpriteOptimizerSettings settings)
        {
            WorldReachIndex.Invalidate();
            WorldReachIndex reach = WorldReachIndex.GetOrBuild(settings);
            var plans = new List<EnemySheetPlan>();

            IEnumerable<string> textures = AssetDatabase.FindAssets("t:Texture2D", EnemyArtRoots.Where(AssetDatabase.IsValidFolder).ToArray())
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct()
                .Where(p => AssetImporter.GetAtPath(p) is TextureImporter ti && ti.textureType == TextureImporterType.Sprite)
                .OrderBy(p => p, StringComparer.Ordinal);

            foreach (string path in textures)
            {
                var plan = new EnemySheetPlan { Path = path };
                plans.Add(plan);

                List<SpriteKey> sprites = AssetDatabase.LoadAllAssetRepresentationsAtPath(path).OfType<Sprite>()
                    .Select(s => SpriteKey.TryGet(s, out SpriteKey k) ? k : default)
                    .Where(k => k.Guid != null)
                    .ToList();
                plan.Worlds.AddRange(sprites.SelectMany(reach.WorldsDrawing).Distinct().OrderBy(w => reach.Worlds.IndexOf(w)));
                bool common = sprites.Any(reach.IsCommon);

                AtlasBinding biome = settings.FolderBindingFor(path);
                if (plan.Worlds.Count == 0 && !common && biome != null && biome.World == null)
                {
                    plan.Placement = EnemySheetPlacement.Biome;
                    plan.Reason = $"no world spawns it yet — stays in {biome.Atlas.name}";
                    continue;
                }

                if (common)
                {
                    plan.Placement = EnemySheetPlacement.Shared;
                    plan.Reason = "left in place: also drawn outside the worlds (heroes / shared configs / chunks / gameplay scene or HUD)" +
                                  (plan.Worlds.Count > 0 ? $" and by {string.Join(", ", plan.Worlds.Select(w => w.name))}" : "");
                    continue;
                }

                if (plan.Worlds.Count == 0)
                {
                    plan.Placement = EnemySheetPlacement.Unused;
                    plan.Destination = UnusedFolder;
                    plan.Reason = "no world's enemies draw it (old / replaced views, unreferenced data)";
                    continue;
                }

                plan.Home = plan.Worlds[0];
                plan.Destination = FolderFor(settings, reach, plan.Home);
                if (plan.Worlds.Count == 1)
                {
                    plan.Placement = EnemySheetPlacement.World;
                    plan.Reason = $"only {plan.Home.name}: {reach.Via(plan.Home, sprites.First(s => reach.WorldsDrawing(s).Contains(plan.Home)))}";
                }
                else
                {
                    plan.Placement = EnemySheetPlacement.BorrowedByOtherWorlds;
                    plan.Reason = $"home {plan.Home.name}; also drawn by " + string.Join("; ", plan.Worlds.Skip(1).Select(w =>
                        $"{w.name} via {reach.Via(w, sprites.First(s => reach.WorldsDrawing(s).Contains(w)))}")) +
                        " — that world loads this atlas too. Give its enemy its own art to split it.";
                }
            }

            return plans;
        }

        private static string FolderFor(SpriteOptimizerSettings settings, WorldReachIndex reach, WorldDefinition world)
        {
            AtlasBinding binding = settings.Atlases.FirstOrDefault(b => b.Atlas != null && b.World == world && b.Folders != null && b.Folders.Count > 0);
            return binding != null ? binding.Folders[0] : WorldFolder(reach, world);
        }

        /// <summary>Moves the files (references are kept: same GUIDs) and points every atlas at the folders.</summary>
        public static void Apply(SpriteOptimizerSettings settings, List<EnemySheetPlan> plans)
        {
            WorldReachIndex reach = WorldReachIndex.GetOrBuild(settings);
            var log = new List<string>();

            // 1. Move files.
            foreach (EnemySheetPlan plan in plans.Where(p => p.Moves))
            {
                SpriteOptimizerUtil.EnsureFolder(plan.Destination);
                string to = AssetDatabase.GenerateUniqueAssetPath($"{plan.Destination}/{plan.Name}");
                string error = AssetDatabase.MoveAsset(plan.Path, to);
                if (!string.IsNullOrEmpty(error))
                {
                    LogHelper.Error(SpriteOptimizerUtil.LogTag, $"Could not move {plan.Path} → {to}: {error}");
                    continue;
                }
                log.Add($"{plan.Path} → {to}");
                plan.Path = to;
            }

            // 2. One atlas per world that has sheets, packing its folder.
            var batch = new AtlasEditBatch();
            List<SpriteAtlas> gameplayAtlases = settings.Atlases.Where(b => b.Atlas != null && b.Context == SpriteContext.Gameplay).Select(b => b.Atlas).ToList();
            foreach (IGrouping<WorldDefinition, EnemySheetPlan> world in plans.Where(p => p.Home != null).GroupBy(p => p.Home))
            {
                string folder = FolderFor(settings, reach, world.Key);
                AtlasBinding binding = settings.Atlases.FirstOrDefault(b => b.Atlas != null && b.World == world.Key)
                                       ?? CreateWorldAtlas(settings, world.Key, reach.Worlds.IndexOf(world.Key) + 1);
                binding.Folders = new List<string> { folder };
                binding.Primary = false;
                if (!gameplayAtlases.Contains(binding.Atlas)) gameplayAtlases.Add(binding.Atlas);
                batch.AddFolder(binding.Atlas, folder);
            }

            // 3. Explicit entries of organized sheets go: their folder packs them now, or (Unused) nothing does.
            foreach (EnemySheetPlan plan in plans.Where(p => p.Placement != EnemySheetPlacement.Biome && p.Placement != EnemySheetPlacement.Shared))
                foreach (SpriteAtlas atlas in gameplayAtlases)
                    batch.RemoveTexture(atlas, plan.Path);

            settings.MarkDirty();
            AssetDatabase.SaveAssetIfDirty(settings);
            batch.Apply();
            WorldReachIndex.Invalidate();

            LogHelper.Log(SpriteOptimizerUtil.LogTag,
                $"Enemy sprites organized: {log.Count} file(s) moved.\n" + string.Join("\n", log));
        }

        private static AtlasBinding CreateWorldAtlas(SpriteOptimizerSettings settings, WorldDefinition world, int number)
        {
            SpriteAtlas template = settings.PrimaryFor(SpriteContext.Gameplay);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{AtlasFolder}/GameplaySprites_W{number}_{world.name.Split('-').Last()}.spriteatlas");
            var atlas = new SpriteAtlas();
            if (template != null)
            {
                atlas.SetPackingSettings(template.GetPackingSettings());
                atlas.SetTextureSettings(template.GetTextureSettings());
                atlas.SetPlatformSettings(template.GetPlatformSettings("DefaultTexturePlatform"));
            }
            atlas.SetIncludeInBuild(true);
            AssetDatabase.CreateAsset(atlas, path);

            var binding = new AtlasBinding { Atlas = atlas, Context = SpriteContext.Gameplay, World = world };
            settings.Atlases.Add(binding);
            return binding;
        }
    }

    /// <summary>Tools ▸ RiftRaiders ▸ Optimize ▸ Organize Enemy Sprites by World — preview the plan, then apply.</summary>
    internal sealed class EnemySpriteOrganizerWindow : EditorWindow
    {
        private List<EnemySheetPlan> _plans;
        private Vector2 _scroll;
        private GUIStyle _wrap;

        [MenuItem("Tools/RiftRaiders/Optimize/Organize Enemy Sprites by World", false, 103)]
        private static void Open() => GetWindow<EnemySpriteOrganizerWindow>("Enemy Sprites by World");

        private void OnGUI()
        {
            _wrap ??= new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };

            EditorGUILayout.LabelField(
                "Moves every enemy sprite sheet into its world's folder (Art/Enemy/World<N>) from what each world's enemies " +
                "actually draw, and makes each world atlas pack its folder. Sheets drawn by several worlds live in the first " +
                "one; sheets also drawn by heroes / shared content / HUD stay where they are; sheets no world draws go to " +
                "Art/Enemy/_Unused (never packed). Moves keep every reference.", _wrap);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Analyze", GUILayout.Height(24f)))
                    _plans = EnemySpriteOrganizer.Analyze(SpriteOptimizerSettings.LoadOrCreate());

                int moves = _plans?.Count(p => p.Moves) ?? 0;
                using (new EditorGUI.DisabledScope(_plans == null))
                {
                    if (GUILayout.Button($"Apply ({moves} move(s) + atlases)", GUILayout.Height(24f))
                        && EditorUtility.DisplayDialog("Organize enemy sprites",
                            $"Move {moves} sprite sheet(s) and update the Gameplay atlases? Revert with git if needed.", "Apply", "Cancel"))
                    {
                        EnemySpriteOrganizer.Apply(SpriteOptimizerSettings.LoadOrCreate(), _plans);
                        _plans = EnemySpriteOrganizer.Analyze(SpriteOptimizerSettings.LoadOrCreate());
                    }
                }
            }

            if (_plans == null) return;

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (IGrouping<EnemySheetPlacement, EnemySheetPlan> group in _plans.GroupBy(p => p.Placement).OrderBy(g => g.Key))
            {
                EditorGUILayout.Space(6f);
                EditorGUILayout.LabelField($"{group.Key} ({group.Count()})", EditorStyles.boldLabel);
                foreach (EnemySheetPlan plan in group)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.ObjectField(AssetDatabase.LoadAssetAtPath<Texture2D>(plan.Path), typeof(Texture2D), false, GUILayout.Width(240f));
                        GUILayout.Label(plan.Moves ? $"→ {plan.Destination}" : "✓ in place", EditorStyles.miniBoldLabel, GUILayout.Width(220f));
                        GUILayout.Label(plan.Reason, _wrap);
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
