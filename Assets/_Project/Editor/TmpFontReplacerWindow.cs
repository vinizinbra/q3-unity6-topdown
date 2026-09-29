using System.Collections.Generic;
using System.Linq;
using QuantumUser.View.Util;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Scans every TMP text (world TextMeshPro + UGUI TextMeshProUGUI, inactive included) in the open
/// scenes — or in the open Prefab Stage, if one is open — and lists each font asset in use with a
/// slot for its replacement: [OldFont -> NewFont]. Each font row folds out into the material
/// presets used with it, so outline/shadow presets can be remapped onto the new font's matching
/// presets (auto-matched by name suffix, e.g. "Old SDF - Outline" -> "New SDF - Outline").
/// Apply is a single undo step.
/// </summary>
public class TmpFontReplacerWindow : EditorWindow
{
    private const string MenuPath = "Tools/RiftRaiders/Utilities/TMP Font Replacer";

    private class MaterialEntry
    {
        public Material Source;
        public Material Target; // null = new font's default material
        public readonly List<TMP_Text> Texts = new();
    }

    private class FontEntry
    {
        public TMP_FontAsset Source;
        public TMP_FontAsset Target;
        public bool Foldout;
        public readonly List<TMP_Text> Texts = new();
        public readonly List<MaterialEntry> Materials = new();

        public TMP_FontAsset EffectiveFont => Target != null ? Target : Source;
        public bool HasChanges => Target != null && Target != Source || Materials.Any(m => m.Target != null && m.Target != m.Source);
    }

    private readonly List<FontEntry> _fonts = new();
    private readonly Dictionary<TMP_FontAsset, Material[]> _presetCache = new();
    private int _missingFontCount;
    private Vector2 _scroll;

    [MenuItem(MenuPath, false, 210)]
    private static void Open()
    {
        var window = GetWindow<TmpFontReplacerWindow>("TMP Font Replacer");
        window.minSize = new Vector2(620, 300);
        window.Scan();
    }

    private void OnEnable() => Scan();

    private void OnHierarchyChange() => Repaint();

    // ---------------------------------------------------------------- scan

    private static List<TMP_Text> CollectTexts(out string scopeLabel)
    {
        var result = new List<TMP_Text>();

        var stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage != null)
        {
            scopeLabel = $"Prefab Stage: {stage.prefabContentsRoot.name}";
            result.AddRange(stage.prefabContentsRoot.GetComponentsInChildren<TMP_Text>(true));
            return result;
        }

        var sceneNames = new List<string>();
        for (var i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;
            sceneNames.Add(scene.name);
            foreach (var root in scene.GetRootGameObjects())
                result.AddRange(root.GetComponentsInChildren<TMP_Text>(true));
        }

        scopeLabel = $"Scene(s): {string.Join(", ", sceneNames)}";
        return result;
    }

    private string _scopeLabel = "";

    private void Scan()
    {
        // Keep the user's picks across rescans.
        var previousFontTargets = _fonts.Where(f => f.Source != null).ToDictionary(f => f.Source, f => f.Target);
        var previousMatTargets = new Dictionary<(TMP_FontAsset, Material), Material>();
        var previousFoldouts = new HashSet<TMP_FontAsset>(_fonts.Where(f => f.Foldout).Select(f => f.Source));
        foreach (var f in _fonts)
        foreach (var m in f.Materials)
            if (m.Source != null) previousMatTargets[(f.Source, m.Source)] = m.Target;

        _fonts.Clear();
        _presetCache.Clear();
        _missingFontCount = 0;

        var byFont = new Dictionary<TMP_FontAsset, FontEntry>();
        foreach (var text in CollectTexts(out _scopeLabel))
        {
            if (text.font == null)
            {
                _missingFontCount++;
                continue;
            }

            if (!byFont.TryGetValue(text.font, out var entry))
            {
                entry = new FontEntry { Source = text.font };
                previousFontTargets.TryGetValue(text.font, out entry.Target);
                entry.Foldout = previousFoldouts.Contains(text.font);
                byFont.Add(text.font, entry);
            }
            entry.Texts.Add(text);

            var mat = text.fontSharedMaterial;
            var matEntry = entry.Materials.FirstOrDefault(m => m.Source == mat);
            if (matEntry == null)
            {
                matEntry = new MaterialEntry { Source = mat };
                if (mat != null) previousMatTargets.TryGetValue((entry.Source, mat), out matEntry.Target);
                entry.Materials.Add(matEntry);
            }
            matEntry.Texts.Add(text);
        }

        _fonts.AddRange(byFont.Values.OrderByDescending(f => f.Texts.Count));
        foreach (var f in _fonts)
            f.Materials.Sort((a, b) => b.Texts.Count.CompareTo(a.Texts.Count));
        Repaint();
    }

    /// <summary>All material presets built on this font's atlas (its default material first).</summary>
    private Material[] GetPresets(TMP_FontAsset font)
    {
        if (font == null) return System.Array.Empty<Material>();
        if (_presetCache.TryGetValue(font, out var cached)) return cached;

        var list = new List<Material>();
        if (font.material != null) list.Add(font.material);

        var fontPath = AssetDatabase.GetAssetPath(font);
        var atlas = font.atlasTexture;
        foreach (var guid in AssetDatabase.FindAssets("t:Material"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (path == fontPath || !AssetDatabase.GetDependencies(path, false).Contains(fontPath)) continue;

            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || list.Contains(mat)) continue;
            if (atlas != null && mat.HasProperty(ShaderUtilities.ID_MainTex) && mat.GetTexture(ShaderUtilities.ID_MainTex) != atlas) continue;
            list.Add(mat);
        }

        var result = list.ToArray();
        _presetCache[font] = result;
        return result;
    }

    /// <summary>"OldFont SDF - Outline" -> the new font's preset named "NewFont SDF - Outline", if any.</summary>
    private Material GuessPreset(FontEntry font, MaterialEntry mat)
    {
        if (font.Target == null || mat.Source == null) return null;
        if (mat.Source == font.Source.material) return null; // default -> default

        var suffix = mat.Source.name.StartsWith(font.Source.name)
            ? mat.Source.name.Substring(font.Source.name.Length)
            : mat.Source.name.Contains(" - ") ? mat.Source.name.Substring(mat.Source.name.IndexOf(" - ")) : null;
        if (string.IsNullOrEmpty(suffix)) return null;

        return GetPresets(font.Target).FirstOrDefault(p => p.name.EndsWith(suffix));
    }

    // ---------------------------------------------------------------- GUI

    private void OnGUI()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.Label(_scopeLabel, EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Rescan", EditorStyles.toolbarButton, GUILayout.Width(60))) Scan();
        }

        if (Application.isPlaying)
            EditorGUILayout.HelpBox("In Play Mode — changes will be lost when you exit.", MessageType.Warning);
        if (_missingFontCount > 0)
            EditorGUILayout.HelpBox($"{_missingFontCount} TMP text(s) have no font assigned (skipped).", MessageType.Info);
        if (_fonts.Count == 0)
        {
            EditorGUILayout.HelpBox("No TextMeshPro texts found.", MessageType.Info);
            return;
        }

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        foreach (var font in _fonts) DrawFont(font);
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Clear All", GUILayout.Height(28), GUILayout.Width(90)))
            {
                foreach (var f in _fonts)
                {
                    f.Target = null;
                    foreach (var m in f.Materials) m.Target = null;
                }
            }

            var changed = _fonts.Where(f => f.HasChanges).ToList();
            using (new EditorGUI.DisabledScope(changed.Count == 0))
            {
                if (GUILayout.Button($"Apply ({changed.Sum(f => f.Texts.Count)} texts)", GUILayout.Height(28)))
                    Apply(changed);
            }
        }
        EditorGUILayout.Space();
    }

    private void DrawFont(FontEntry font)
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                font.Foldout = GUILayout.Toggle(font.Foldout, GUIContent.none, EditorStyles.foldout, GUILayout.Width(14));

                // Not disabled so clicking it still pings the asset; edits are ignored.
                EditorGUILayout.ObjectField(font.Source, typeof(TMP_FontAsset), false);
                GUILayout.Label("→", GUILayout.Width(16));

                EditorGUI.BeginChangeCheck();
                var newTarget = (TMP_FontAsset)EditorGUILayout.ObjectField(font.Target, typeof(TMP_FontAsset), false);
                if (EditorGUI.EndChangeCheck())
                {
                    font.Target = newTarget;
                    foreach (var m in font.Materials) m.Target = GuessPreset(font, m);
                }

                GUILayout.Label($"{font.Texts.Count} texts", EditorStyles.miniLabel, GUILayout.Width(55));
                if (GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(48)))
                    Selection.objects = font.Texts.Where(t => t != null).Select(t => (Object)t.gameObject).ToArray();
            }

            if (!font.Foldout) return;

            EditorGUI.indentLevel++;
            var presets = GetPresets(font.EffectiveFont);
            var options = new[] { font.Target == null ? "<Keep>" : "<Font Default Material>" }
                .Concat(presets.Select(p => p.name)).ToArray();

            foreach (var mat in font.Materials)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(18);
                    EditorGUILayout.ObjectField(mat.Source, typeof(Material), false);
                    GUILayout.Label("→", GUILayout.Width(16));

                    var index = mat.Target == null ? 0 : System.Array.IndexOf(presets, mat.Target) + 1;
                    var pick = EditorGUILayout.Popup(index, options);
                    mat.Target = pick == 0 ? null : presets[pick - 1];

                    GUILayout.Label($"{mat.Texts.Count} texts", EditorStyles.miniLabel, GUILayout.Width(55));
                    if (GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(48)))
                        Selection.objects = mat.Texts.Where(t => t != null).Select(t => (Object)t.gameObject).ToArray();
                }
            }
            EditorGUI.indentLevel--;
        }
    }

    // ---------------------------------------------------------------- apply

    private void Apply(List<FontEntry> fonts)
    {
        Undo.IncrementCurrentGroup();
        var group = Undo.GetCurrentGroup();
        var dirtyScenes = new HashSet<Scene>();
        var count = 0;

        foreach (var font in fonts)
        foreach (var mat in font.Materials)
        {
            // Font swapped without a preset pick -> new font's default material (the old preset
            // points at the old atlas and would render garbage).
            var targetMat = mat.Target != null ? mat.Target : font.Target != null ? font.Target.material : null;

            foreach (var text in mat.Texts)
            {
                if (text == null) continue;
                Undo.RecordObject(text, "Replace TMP Font");
                if (font.Target != null && text.font != font.Target) text.font = font.Target;
                if (targetMat != null) text.fontSharedMaterial = targetMat;
                text.SetAllDirty();
                EditorUtility.SetDirty(text);
                PrefabUtility.RecordPrefabInstancePropertyModifications(text);
                dirtyScenes.Add(text.gameObject.scene);
                count++;
            }
        }

        Undo.CollapseUndoOperations(group);
        Undo.SetCurrentGroupName("Replace TMP Fonts");
        if (!Application.isPlaying)
            foreach (var scene in dirtyScenes)
                if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);

        LogHelper.Log("UI", $"TMP Font Replacer updated {count} text(s).");

        // Reset picks for the fonts that were applied; everything is re-grouped by the rescan.
        foreach (var font in fonts)
        {
            font.Target = null;
            foreach (var m in font.Materials) m.Target = null;
        }
        Scan();
    }
}
