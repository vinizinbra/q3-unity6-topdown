using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.U2D;
using UnityEngine.UI;

/// <summary>
/// Scans the currently open scene(s) for every Sprite in use (via Image/SpriteRenderer) and
/// buckets each one by whether it sits under a Canvas (UI) or not (gameplay/world), so the two
/// groups can be fed into separate Sprite Atlases.
/// </summary>
public sealed class SpriteAtlasScannerWindow : EditorWindow
{
    private const string AtlasFolder = "Assets/_Project/Art/SpriteAtlases";

    private readonly List<SpriteUsage> _uiSprites = new List<SpriteUsage>();
    private readonly List<SpriteUsage> _gameplaySprites = new List<SpriteUsage>();
    private Vector2 _scroll;
    private bool _uiFoldout = true;
    private bool _gameplayFoldout = true;

    [MenuItem("Tools/Art/Sprite Atlas Scanner")]
    private static void Open()
    {
        var window = GetWindow<SpriteAtlasScannerWindow>("Sprite Atlas Scanner");
        window.minSize = new Vector2(480f, 360f);
        window.Scan();
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Lists every Sprite referenced by an Image or SpriteRenderer in the open scene(s), split by " +
            "whether it sits under a Canvas (UI) or not (gameplay/world). Includes inactive objects. " +
            "Use it to decide what goes into a UI atlas vs a gameplay atlas.",
            MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Rescan", GUILayout.Height(24f)))
                Scan();
            using (new EditorGUI.DisabledScope(_uiSprites.Count == 0 && _gameplaySprites.Count == 0))
            {
                if (GUILayout.Button("Create / Update Sprite Atlases", GUILayout.Height(24f)))
                    CreateOrUpdateAtlases();
            }
        }

        EditorGUILayout.Space();
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        DrawGroup(ref _uiFoldout, "UI Sprites (inside a Canvas)", _uiSprites);
        EditorGUILayout.Space();
        DrawGroup(ref _gameplayFoldout, "Gameplay Sprites (outside a Canvas)", _gameplaySprites);
        EditorGUILayout.EndScrollView();
    }

    private void DrawGroup(ref bool foldout, string title, List<SpriteUsage> entries)
    {
        int uniqueTextures = entries.Select(e => e.TexturePath).Distinct().Count();
        foldout = EditorGUILayout.Foldout(foldout, $"{title} — {entries.Count} sprite(s), {uniqueTextures} texture(s)", true);
        if (!foldout)
            return;

        string lastTexturePath = null;
        using (new EditorGUI.IndentLevelScope())
        {
            foreach (SpriteUsage usage in entries)
            {
                if (usage.TexturePath != lastTexturePath)
                {
                    lastTexturePath = usage.TexturePath;
                    EditorGUILayout.LabelField(usage.TexturePath, EditorStyles.miniBoldLabel);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.ObjectField(usage.Sprite, typeof(Sprite), false);
                    GUILayout.Label($"{usage.Users.Count} use(s)", GUILayout.Width(60f));
                    if (GUILayout.Button("Select", GUILayout.Width(50f)))
                    {
                        Selection.objects = usage.Users.ToArray();
                        EditorGUIUtility.PingObject(usage.Sprite);
                    }
                }
            }
        }
    }

    private void Scan()
    {
        _uiSprites.Clear();
        _gameplaySprites.Clear();

        var uiBySprite = new Dictionary<Sprite, SpriteUsage>();
        var gameplayBySprite = new Dictionary<Sprite, SpriteUsage>();

        for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
        {
            Scene scene = SceneManager.GetSceneAt(sceneIndex);
            if (!scene.isLoaded)
                continue;

            foreach (GameObject root in scene.GetRootGameObjects())
                ScanTransform(root.transform, false, uiBySprite, gameplayBySprite);
        }

        _uiSprites.AddRange(uiBySprite.Values.OrderBy(u => u.TexturePath).ThenBy(u => u.Sprite.name));
        _gameplaySprites.AddRange(gameplayBySprite.Values.OrderBy(u => u.TexturePath).ThenBy(u => u.Sprite.name));
        Repaint();
    }

    private static void ScanTransform(
        Transform node,
        bool underCanvas,
        Dictionary<Sprite, SpriteUsage> ui,
        Dictionary<Sprite, SpriteUsage> gameplay)
    {
        bool inCanvas = underCanvas || node.GetComponent<Canvas>() != null;
        Dictionary<Sprite, SpriteUsage> bucket = inCanvas ? ui : gameplay;

        var image = node.GetComponent<Image>();
        if (image != null && image.sprite != null)
            RecordSprite(image.sprite, node.gameObject, bucket);

        var spriteRenderer = node.GetComponent<SpriteRenderer>();
        if (spriteRenderer != null && spriteRenderer.sprite != null)
            RecordSprite(spriteRenderer.sprite, node.gameObject, bucket);

        for (int i = 0; i < node.childCount; i++)
            ScanTransform(node.GetChild(i), inCanvas, ui, gameplay);
    }

    private static void RecordSprite(Sprite sprite, GameObject owner, Dictionary<Sprite, SpriteUsage> bucket)
    {
        string path = AssetDatabase.GetAssetPath(sprite);
        if (string.IsNullOrEmpty(path))
            return; // procedurally-created sprite with no backing asset - nothing to atlas

        if (!bucket.TryGetValue(sprite, out SpriteUsage usage))
        {
            Texture2D texture = AssetDatabase.LoadMainAssetAtPath(path) as Texture2D;
            usage = new SpriteUsage(sprite, texture, path);
            bucket.Add(sprite, usage);
        }

        usage.Users.Add(owner);
    }

    private void CreateOrUpdateAtlases()
    {
        EnsureFolderExists(AtlasFolder);
        CreateOrUpdateAtlas("UISprites", _uiSprites);
        CreateOrUpdateAtlas("GameplaySprites", _gameplaySprites);
        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("Sprite Atlas Scanner",
            $"Atlases created/updated in '{AtlasFolder}'.", "OK");
    }

    private static void CreateOrUpdateAtlas(string atlasName, List<SpriteUsage> entries)
    {
        if (entries.Count == 0)
            return;

        string path = $"{AtlasFolder}/{atlasName}.spriteatlas";
        SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
        if (atlas == null)
        {
            atlas = new SpriteAtlas();
            AssetDatabase.CreateAsset(atlas, path);
        }

        var existing = new HashSet<Object>(atlas.GetPackables());
        var texturesToAdd = entries
            .Select(e => (Object)e.Texture)
            .Where(t => t != null)
            .Distinct()
            .Where(t => !existing.Contains(t))
            .ToArray();

        if (texturesToAdd.Length > 0)
        {
            atlas.Add(texturesToAdd);
            EditorUtility.SetDirty(atlas);
        }
    }

    private static void EnsureFolderExists(string folderPath)
    {
        string[] parts = folderPath.Split('/');
        string currentPath = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string nextPath = currentPath + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(nextPath))
                AssetDatabase.CreateFolder(currentPath, parts[i]);
            currentPath = nextPath;
        }
    }

    private sealed class SpriteUsage
    {
        public readonly Sprite Sprite;
        public readonly Texture2D Texture;
        public readonly string TexturePath;
        public readonly List<GameObject> Users = new List<GameObject>();

        public SpriteUsage(Sprite sprite, Texture2D texture, string texturePath)
        {
            Sprite = sprite;
            Texture = texture;
            TexturePath = texturePath;
        }
    }
}
