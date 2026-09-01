using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class IsolateSelection
{
    private const string PrefsKey = "IsolateSelection_Data";

    private static bool _isIsolated;
    private static Dictionary<int, bool> _previousStates = new();
    private static HashSet<int> _isolatedRoots = new();

    private static GUIContent _warningIcon;

    static IsolateSelection()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorSceneManager.activeSceneChangedInEditMode += OnSceneChanged;
        EditorSceneManager.sceneClosing += OnSceneClosing;
        EditorSceneManager.sceneSaving += OnSceneSaving;
        EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyItemGUI;
        SceneView.duringSceneGui += OnSceneGUI;

        if (EditorPrefs.HasKey(PrefsKey))
            Deserialize();
    }

    [MenuItem("Tools/Toggle Isolate Selection %e")]
    public static void Toggle()
    {
        if (_isIsolated)
            RestoreAll();
        else
            Isolate();
    }

    private static void Isolate()
    {
        var selected = Selection.gameObjects;
        if (selected == null || selected.Length == 0)
        {
            Debug.LogWarning("[Isolate] Nothing selected.");
            return;
        }

        _previousStates.Clear();
        _isolatedRoots.Clear();

        var keepActive = new HashSet<int>();
        foreach (var go in selected)
        {
            var root = go.transform.root.gameObject;
            keepActive.Add(root.GetInstanceID());
            _isolatedRoots.Add(root.GetInstanceID());
        }

        // Deactivate sibling roots
        var roots = SceneManager.GetActiveScene().GetRootGameObjects();
        foreach (var root in roots)
        {
            int id = root.GetInstanceID();
            _previousStates[id] = root.activeSelf;
            root.SetActive(keepActive.Contains(id));
        }

        // Deactivate siblings at each ancestor level (but not the ancestors themselves)
        foreach (var go in selected)
        {
            var t = go.transform;
            while (t.parent != null)
            {
                foreach (Transform sibling in t.parent)
                {
                    if (sibling == t) continue;
                    int id = sibling.gameObject.GetInstanceID();
                    if (!_previousStates.ContainsKey(id))
                        _previousStates[id] = sibling.gameObject.activeSelf;
                    sibling.gameObject.SetActive(false);
                }
                t = t.parent;
            }
        }

        _isIsolated = true;
        Serialize();
        SceneView.RepaintAll();
        EditorApplication.RepaintHierarchyWindow();
    }

    public static void RestoreAll()
    {
        foreach (var kv in _previousStates)
        {
            var obj = EditorUtility.InstanceIDToObject(kv.Key) as GameObject;
            if (obj != null)
                obj.SetActive(kv.Value);
        }

        _previousStates.Clear();
        _isolatedRoots.Clear();
        _isIsolated = false;
        EditorPrefs.DeleteKey(PrefsKey);
        SceneView.RepaintAll();
        EditorApplication.RepaintHierarchyWindow();
    }

    // --- Hierarchy: warning icon + yellow highlight on isolated items ---
    private static void OnHierarchyItemGUI(int instanceID, Rect selectionRect)
    {
        if (!_isIsolated) return;
        if (!_isolatedRoots.Contains(instanceID)) return;

        if (_warningIcon == null)
            _warningIcon = EditorGUIUtility.IconContent("console.warnicon.sml");

        // Yellow background highlight
        Color bgColor = new Color(1f, 0.85f, 0f, 0.15f);
        EditorGUI.DrawRect(selectionRect, bgColor);

        // Yellow left edge bar
        Rect barRect = new Rect(selectionRect.x, selectionRect.y, 3, selectionRect.height);
        EditorGUI.DrawRect(barRect, new Color(1f, 0.75f, 0f, 1f));

        // Warning icon on the right
        Rect iconRect = new Rect(selectionRect.xMax - 18, selectionRect.y, 16, selectionRect.height);
        var prevColor = GUI.color;
        GUI.color = new Color(1f, 0.85f, 0f);
        GUI.Label(iconRect, _warningIcon);
        GUI.color = prevColor;
    }

    // --- SceneView: warning banner ---
    private static void OnSceneGUI(SceneView sceneView)
    {
        if (!_isIsolated) return;

        Handles.BeginGUI();

        float width = 280;
        float height = 26;
        float x = (sceneView.position.width - width) / 2f;
        float y = 6;

        // Yellow/gold warning background
        EditorGUI.DrawRect(new Rect(x, y, width, height), new Color(1f, 0.75f, 0f, 0.9f));

        var style = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 12,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.black }
        };

        GUI.Label(new Rect(x, y, width, height), "ISOLATED MODE  (Ctrl+E to exit)", style);

        Handles.EndGUI();
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode && _isIsolated)
            RestoreAll();
    }

    private static void OnSceneChanged(Scene prev, Scene next)
    {
        // Force-clear isolation without trying to restore objects from the old scene
        _previousStates.Clear();
        _isolatedRoots.Clear();
        _isIsolated = false;
        EditorPrefs.DeleteKey(PrefsKey);
    }

    private static void OnSceneClosing(Scene scene, bool removingScene)
    {
        if (_isIsolated) RestoreAll();
    }

    private static void OnSceneSaving(Scene scene, string path)
    {
        if (_isIsolated) RestoreAll();
    }

    private static void Serialize()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var kv in _previousStates)
        {
            sb.Append(kv.Key);
            sb.Append(':');
            sb.Append(kv.Value);
            sb.Append(',');
        }

        if (sb.Length > 0)
            sb.Remove(sb.Length - 1, 1);

        EditorPrefs.SetString(PrefsKey, sb.ToString());
    }

    private static void Deserialize()
    {
        _previousStates.Clear();

        string[] entries = EditorPrefs.GetString(PrefsKey, "").Split(',');
        foreach (var entry in entries)
        {
            string[] pair = entry.Split(':');
            if (pair.Length < 2) continue;
            if (!int.TryParse(pair[0], out int id)) continue;
            if (!bool.TryParse(pair[1], out bool wasActive)) continue;

            var obj = EditorUtility.InstanceIDToObject(id) as GameObject;
            if (obj != null)
                _previousStates[id] = wasActive;
        }

        if (_previousStates.Count > 0)
        {
            _isIsolated = true;
            RestoreAll();
        }
        else
        {
            EditorPrefs.DeleteKey(PrefsKey);
        }
    }
}
