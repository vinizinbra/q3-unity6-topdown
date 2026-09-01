using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Searchable multi-select popup over every AudioClip in the project, opened from the "+" button
// SoundDataPickerPropertyDrawer draws next to a SoundData field. The point: turn "find some clips,
// create an asset, name it, set the group, drag each clip in one at a time" into "search, tick a
// few boxes, Create".
//
// AudioClip objects are NOT loaded for the full list - with well over ten thousand clips in this
// project (mostly third-party SFX packs), loading every one just to read its name would be slow
// and wasteful. Only paths are read up front (AssetDatabase.FindAssets/GUIDToAssetPath, string
// only); a clip is loaded only when actually previewed or ticked.
public class SoundClipPickerWindow : EditorWindow
{
    private struct ClipEntry
    {
        public string Path;
        public string Name;
    }

    // IMGUI rendering a row per result dies well before this project's full clip count - cap what
    // actually draws and point the user at narrowing the search instead.
    private const int MaxDisplayedResults = 200;

    private string _folder;
    private Action<SoundData> _onCreated;

    private List<ClipEntry> _allClips = new List<ClipEntry>();
    private readonly List<ClipEntry> _filtered = new List<ClipEntry>();
    private string _search = "";

    // Order preserved (not a HashSet) so the Selected list reads in the order clips were ticked.
    private readonly List<string> _selectedPaths = new List<string>();
    private string _assetName = "NewSound";
    private SoundGroup _group = SoundGroup.Sfx;

    private Vector2 _resultsScroll;
    private Vector2 _selectedScroll;

    public static void Open(string folder, Action<SoundData> onCreated)
    {
        var window = CreateInstance<SoundClipPickerWindow>();
        window.titleContent = new GUIContent("Pick Sound Clips");
        window._folder = string.IsNullOrEmpty(folder) ? "Assets/_Project/Audio/Generated" : folder;
        window._onCreated = onCreated;
        window.PopulateAllClips();
        window.minSize = new Vector2(420f, 480f);
        window.ShowUtility();
    }

    private void OnDisable()
    {
        StopPreview();
    }

    private void PopulateAllClips()
    {
        var guids = AssetDatabase.FindAssets("t:AudioClip");
        _allClips = new List<ClipEntry>(guids.Length);
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path))
                continue;

            _allClips.Add(new ClipEntry { Path = path, Name = Path.GetFileNameWithoutExtension(path) });
        }

        _allClips.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        RefreshFiltered();
    }

    private void RefreshFiltered()
    {
        _filtered.Clear();

        // Ticked clips stay pinned at the top, sorted (both lists are built off the already
        // alphabetical _allClips), and visible regardless of the search text - so refining the
        // search to find one more clip can never scroll a pick out of view or make it look dropped.
        foreach (var entry in _allClips)
        {
            if (_selectedPaths.Contains(entry.Path))
                _filtered.Add(entry);
        }

        foreach (var entry in _allClips)
        {
            if (_selectedPaths.Contains(entry.Path))
                continue;

            if (string.IsNullOrEmpty(_search) || entry.Name.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0)
                _filtered.Add(entry);
        }
    }

    private void OnGUI()
    {
        DrawSelectedSection();

        EditorGUILayout.Space();
        GUILayout.Label($"All Clips ({_allClips.Count})", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginChangeCheck();
        _search = EditorGUILayout.TextField("Search", _search);
        if (EditorGUI.EndChangeCheck())
            RefreshFiltered();

        if (GUILayout.Button("Stop All", GUILayout.Width(70f)))
            StopPreview();
        EditorGUILayout.EndHorizontal();

        DrawResultsList();

        EditorGUILayout.Space();
        DrawCreateBar();
    }

    private void DrawSelectedSection()
    {
        GUILayout.Label($"Selected ({_selectedPaths.Count})", EditorStyles.boldLabel);

        if (_selectedPaths.Count == 0)
        {
            EditorGUILayout.HelpBox("Tick clips below - one is fine, several turns on randomized picking.", MessageType.None);
            return;
        }

        _selectedScroll = EditorGUILayout.BeginScrollView(_selectedScroll, GUILayout.MaxHeight(120f));
        for (var i = _selectedPaths.Count - 1; i >= 0; i--)
        {
            var path = _selectedPaths[i];
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(Path.GetFileNameWithoutExtension(path));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Play", GUILayout.Width(40f)))
                PlayPreview(path);
            if (GUILayout.Button("x", GUILayout.Width(20f)))
            {
                _selectedPaths.RemoveAt(i);
                RefreshFiltered();
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();
    }

    private void DrawResultsList()
    {
        _resultsScroll = EditorGUILayout.BeginScrollView(_resultsScroll);

        // The pinned selected clips (always the leading run of _filtered - see RefreshFiltered)
        // are exempt from the cap, so ticking past MaxDisplayedResults picks can never make an
        // earlier pick fall off the visible list.
        var shown = Mathf.Max(_selectedPaths.Count, Mathf.Min(_filtered.Count, MaxDisplayedResults));
        shown = Mathf.Min(shown, _filtered.Count);

        // Selection changes are applied AFTER the loop, never mid-loop - RefreshFiltered rebuilds
        // _filtered itself, and mutating the list this loop is reading from partway through would
        // read back its own just-changed order for the remaining rows of this same pass.
        var selectionChanged = false;

        for (var i = 0; i < shown; i++)
        {
            var entry = _filtered[i];
            EditorGUILayout.BeginHorizontal();

            var isSelected = _selectedPaths.Contains(entry.Path);
            var nowSelected = EditorGUILayout.ToggleLeft(entry.Name, isSelected);
            if (nowSelected != isSelected)
            {
                if (nowSelected)
                    _selectedPaths.Add(entry.Path);
                else
                    _selectedPaths.Remove(entry.Path);

                selectionChanged = true;
            }

            if (GUILayout.Button("Play", GUILayout.Width(40f)))
                PlayPreview(entry.Path);

            EditorGUILayout.EndHorizontal();
        }

        if (selectionChanged)
            RefreshFiltered();

        if (_filtered.Count > shown)
            EditorGUILayout.HelpBox($"{_filtered.Count - shown} more match - refine your search to see them.", MessageType.Info);

        EditorGUILayout.EndScrollView();
    }

    private void DrawCreateBar()
    {
        _assetName = EditorGUILayout.TextField("Asset Name", _assetName);
        _group = (SoundGroup)EditorGUILayout.EnumPopup("Group", _group);
        EditorGUILayout.LabelField("Folder", _folder);

        GUI.enabled = _selectedPaths.Count > 0 && !string.IsNullOrWhiteSpace(_assetName);
        if (GUILayout.Button("Create Sound Data", GUILayout.Height(28f)))
            CreateAndClose();
        GUI.enabled = true;
    }

    private void CreateAndClose()
    {
        StopPreview();

        var clips = _selectedPaths
            .Select(p => AssetDatabase.LoadAssetAtPath<AudioClip>(p))
            .Where(c => c != null)
            .ToArray();

        if (clips.Length == 0)
            return;

        EnsureFolderExists(_folder);

        var asset = CreateInstance<SoundData>();
        asset.group = _group;
        asset.variants = clips.Select(c => new SoundClip(c)).ToArray();
        asset.pick = SoundData.PickMode.RandomNoRepeat;

        // A single clip has no variation to pick between, so leave its pitch flat rather than
        // quietly detuning a one-off sound - same convention SoundDataCreator uses.
        if (clips.Length < 2)
            asset.pitch = Vector2.one;

        var path = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(_folder, _assetName + ".asset"));
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();

        _onCreated?.Invoke(asset);
        Close();
    }

    private static void EnsureFolderExists(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
            return;

        var parts = folder.Split('/');
        var current = parts[0];
        for (var i = 1; i < parts.Length; i++)
        {
            var next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    // Editor audio preview lives on the internal UnityEditor.AudioUtil. Reflected rather than
    // referenced directly since it isn't part of the public API - same approach as
    // AudioSilenceSplitterWindow's own preview playback.
    private static void PlayPreview(string path)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        if (clip == null)
            return;

        InvokeAudioUtil("StopAllPreviewClips", Array.Empty<object>());
        InvokeAudioUtil("PlayPreviewClip", new object[] { clip, 0, false }, typeof(AudioClip), typeof(int), typeof(bool));
    }

    private static void StopPreview()
    {
        InvokeAudioUtil("StopAllPreviewClips", Array.Empty<object>());
    }

    private static void InvokeAudioUtil(string method, object[] args, params Type[] signature)
    {
        var type = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
        var info = type?.GetMethod(method, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, signature, null);

        if (info == null)
        {
            Debug.LogWarning($"[SoundClipPickerWindow] UnityEditor.AudioUtil.{method} not found on this Unity version - preview playback is unavailable.");
            return;
        }

        info.Invoke(null, args);
    }
}
