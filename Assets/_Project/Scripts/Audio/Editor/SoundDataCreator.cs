using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Project-window workflow for authoring SoundData: select one or more AudioClips, right-click ->
// Create -> Audio -> <category>, and get a ready-to-use asset filed in that category's folder with
// the clips already assigned and the SoundGroup already set.
//
// Selecting SEVERAL clips is the point - that is how a randomised variation set gets made, which is
// otherwise the most tedious part of authoring audio (create asset, name it, set the group, then
// drag clips in one at a time).
//
// One folder per SoundGroup, so the mix bus a sound belongs to and where it lives on disk are the
// same decision - there is no second taxonomy to keep in sync, and nothing ends up scattered.
internal static class SoundDataCreator
{
    private const string RootFolder = "Assets/_Project/Audio";

    // Menu order is grouped by how often each is authored, not alphabetically - Weapons/Heroes/
    // Enemies carry most of a run's sound, and Sfx is the catch-all people reach for constantly.
    [MenuItem("Assets/Create/Audio/Weapons Sound", false, 10)]
    private static void CreateWeapons() => Create(SoundGroup.Weapons);

    [MenuItem("Assets/Create/Audio/Heroes Sound", false, 11)]
    private static void CreateHeroes() => Create(SoundGroup.Heroes);

    [MenuItem("Assets/Create/Audio/Enemies Sound", false, 12)]
    private static void CreateEnemies() => Create(SoundGroup.Enemies);

    [MenuItem("Assets/Create/Audio/Impacts Sound", false, 13)]
    private static void CreateImpacts() => Create(SoundGroup.Impacts);

    [MenuItem("Assets/Create/Audio/Generic Sfx Sound", false, 24)]
    private static void CreateSfx() => Create(SoundGroup.Sfx);

    [MenuItem("Assets/Create/Audio/Pickups Sound", false, 25)]
    private static void CreatePickups() => Create(SoundGroup.Pickups);

    [MenuItem("Assets/Create/Audio/UI Sound", false, 26)]
    private static void CreateUi() => Create(SoundGroup.Ui);

    [MenuItem("Assets/Create/Audio/Voice Sound", false, 37)]
    private static void CreateVoice() => Create(SoundGroup.Voice);

    [MenuItem("Assets/Create/Audio/Ambience Sound", false, 38)]
    private static void CreateAmbience() => Create(SoundGroup.Ambience);

    [MenuItem("Assets/Create/Audio/Music", false, 39)]
    private static void CreateMusic() => Create(SoundGroup.Music);

    private static void Create(SoundGroup group)
    {
        AudioClip[] clips = Selection.GetFiltered<AudioClip>(SelectionMode.Assets)
            .OrderBy(c => c.name, System.StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var asset = ScriptableObject.CreateInstance<SoundData>();
        asset.group = group;
        asset.clips = clips;

        // A single clip has no variation to pick between, so leave its pitch flat rather than
        // quietly detuning a one-off sound. Multiple clips are a variation set by definition and
        // want the jitter on from the start - that is the whole reason they were selected together.
        if (clips.Length < 2)
            asset.pitch = Vector2.one;

        // Only the two categories that are always player-produced. Enemies/Impacts/Pickups happen
        // TO a player rather than being caused by one, so "was this mine" doesn't apply and the
        // flag would just be a misleading tick on the asset.
        asset.quieterWhenRemote = group == SoundGroup.Heroes || group == SoundGroup.Weapons;

        // Music, UI and voice are heard flat wherever they happen - a menu click has no world
        // position, and a hero bark should not quieten because the camera drifted. Everything else
        // is positional by default, which is the common case for gameplay sound.
        asset.spatial = group != SoundGroup.Music
                        && group != SoundGroup.Ui
                        && group != SoundGroup.Voice
                        && group != SoundGroup.Ambience;

        // Music and ambience are the two groups that are essentially always looping and want a fade
        // rather than a hard cut. Everything else stays at the defaults.
        if (group == SoundGroup.Music || group == SoundGroup.Ambience)
        {
            asset.loop = true;
            asset.fadeIn = 1f;
            asset.fadeOut = 1f;
            asset.maxConcurrent = 1;
        }

        string folder = EnsureFolder(group);
        string path = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(folder, ResolveName(clips, group) + ".asset"));

        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();

        // Selected and focused with the name field live, so it can be renamed immediately - the
        // derived name below is a good guess, not a decision.
        Selection.activeObject = asset;
        EditorUtility.FocusProjectWindow();
        ProjectWindowUtil.ShowCreatedAsset(asset);
    }

    private static string EnsureFolder(SoundGroup group)
    {
        if (!AssetDatabase.IsValidFolder(RootFolder))
        {
            string parent = Path.GetDirectoryName(RootFolder).Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, Path.GetFileName(RootFolder));
        }

        string folder = $"{RootFolder}/{group}";
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder(RootFolder, group.ToString());

        return folder;
    }

    // Names the asset after what the selected clips have in common, so a set of
    // Pistol_Fire_01/02/03 becomes "Pistol_Fire" rather than the first clip's full name or a
    // generic "NewSound" that has to be retyped every time.
    private static string ResolveName(AudioClip[] clips, SoundGroup group)
    {
        if (clips.Length == 0)
            return $"New{group}Sound";

        if (clips.Length == 1)
            return clips[0].name;

        string prefix = clips[0].name;
        for (int i = 1; i < clips.Length; i++)
            prefix = CommonPrefix(prefix, clips[i].name);

        // Strip the separator/index tail the common prefix stops on ("Pistol_Fire_0" -> "Pistol_Fire").
        prefix = prefix.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '_', '-', ' ', '.');

        return prefix.Length >= 3 ? prefix : clips[0].name;
    }

    private static string CommonPrefix(string a, string b)
    {
        int length = Mathf.Min(a.Length, b.Length);
        int i = 0;
        while (i < length && char.ToLowerInvariant(a[i]) == char.ToLowerInvariant(b[i]))
            i++;

        return a.Substring(0, i);
    }
}
