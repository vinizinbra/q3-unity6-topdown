using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Toolbars;
using UnityEngine;

/// <summary>
/// Main Editor toolbar dropdown "Meta: Normal / Playtest" - switches the META_PLAYTEST define (see
/// MetaProgressionPlaytestConfig) for the active build target. Normal = the real saved progression; Playtest =
/// GameDB.PlaytestConfig's levels act as a floor. Changing it recompiles; the toolbar is rebuilt afterwards and
/// reads the define back, so the label always shows what's compiled in.
/// </summary>
public static class MetaProgressionPlaytestMenu
{
    private const string Define = "META_PLAYTEST";
    private const string ToolbarPath = "RiftRaiders/Meta Progression";

    [MainToolbarElement(ToolbarPath, defaultDockPosition = MainToolbarDockPosition.Left)]
    private static IEnumerable<MainToolbarElement> CreateDropdown()
    {
        bool playtest = IsPlaytest();
        var content = new MainToolbarContent(playtest ? "Meta: Playtest" : "Meta: Normal",
            "Meta progression source: Normal = saved progression, Playtest = GameDB.PlaytestConfig levels as a floor (META_PLAYTEST define).");
        yield return new MainToolbarDropdown(content, ShowMenu);
    }

    private static void ShowMenu(Rect rect)
    {
        bool playtest = IsPlaytest();
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("Normal"), !playtest, () => SetPlaytest(false));
        menu.AddItem(new GUIContent("Playtest"), playtest, () => SetPlaytest(true));
        menu.AddSeparator(string.Empty);
        menu.AddItem(new GUIContent("Edit Playtest Profile"), false, SelectProfile);
        menu.DropDown(rect);
    }

    private static void SetPlaytest(bool playtest)
    {
        if (playtest == IsPlaytest())
            return;

        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[MetaProgression] stop Play mode before switching Normal/Playtest (it recompiles).");
            return;
        }

        NamedBuildTarget target = CurrentTarget();
        List<string> defines = PlayerSettings.GetScriptingDefineSymbols(target).Split(';').Where(d => d.Length > 0).ToList();
        if (playtest)
            defines.Add(Define);
        else
            defines.Remove(Define);
        PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", defines));
        MainToolbar.Refresh(ToolbarPath);
    }

    private static void SelectProfile()
    {
        GameDB db = AssetDatabase.LoadAssetAtPath<GameDB>("Assets/_Project/Resources/GameDB.asset");
        if (db != null && db.PlaytestConfig != null)
        {
            Selection.activeObject = db.PlaytestConfig;
            EditorGUIUtility.PingObject(db.PlaytestConfig);
        }
    }

    private static bool IsPlaytest()
    {
        return PlayerSettings.GetScriptingDefineSymbols(CurrentTarget()).Split(';').Contains(Define);
    }

    private static NamedBuildTarget CurrentTarget()
    {
        return NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget));
    }
}
