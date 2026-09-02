using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;
using QuantumUser.View.Util;

// Two main-toolbar buttons for the DebugStartLevelUpCount cheat flow (see ChooseWindow's own
// debugSkipIntroAnimation field / GameplayUiController's upgradeTimeScaleRampInDuration/
// upgradeTimeScaleRampOutDuration) - flips every ChooseWindow/GameplayUiController instance
// currently in the loaded scene(s) between "skip the intro animation and Time.timeScale ramp
// entirely" and back to their authored defaults, so chaining several debug level-ups back to back
// doesn't cost ~1s+ per screen sitting through the reveal. Works in both Edit and Play mode - right
// click the main toolbar to show/hide these under the "RiftRaiders" group if they're not visible.
static class UpgradeScreenDebugToolbar
{
    private const float DefaultRampInDuration = 0.4f;
    private const float DefaultRampOutDuration = 0.15f;

    [MainToolbarElement("RiftRaiders/Disable Upgrade Screen Animation", defaultDockPosition = MainToolbarDockPosition.Right)]
    static MainToolbarButton CreateDisableButton()
    {
        return new MainToolbarButton(
            new MainToolbarContent("Upgrade FX Off", "Skip the upgrade screen's intro animation and Time.timeScale ramp entirely - for testing DebugStartLevelUpCount without waiting through each screen's reveal."),
            () => SetUpgradeScreenDebugState(skipAnimation: true, rampInDuration: 0f, rampOutDuration: 0f));
    }

    [MainToolbarElement("RiftRaiders/Reset Upgrade Screen Animation", defaultDockPosition = MainToolbarDockPosition.Right)]
    static MainToolbarButton CreateResetButton()
    {
        return new MainToolbarButton(
            new MainToolbarContent("Upgrade FX Reset", "Restore the upgrade screen's intro animation and Time.timeScale ramp to their authored defaults."),
            () => SetUpgradeScreenDebugState(skipAnimation: false, rampInDuration: DefaultRampInDuration, rampOutDuration: DefaultRampOutDuration));
    }

    private static void SetUpgradeScreenDebugState(bool skipAnimation, float rampInDuration, float rampOutDuration)
    {
        int windowCount = ApplyToAll<ChooseWindow>(so =>
        {
            so.FindProperty("debugSkipIntroAnimation").boolValue = skipAnimation;
        });

        int controllerCount = ApplyToAll<GameplayUiController>(so =>
        {
            so.FindProperty("upgradeTimeScaleRampInDuration").floatValue = rampInDuration;
            so.FindProperty("upgradeTimeScaleRampOutDuration").floatValue = rampOutDuration;
        });

        if (windowCount == 0 && controllerCount == 0)
        {
            LogHelper.Warn("Debug", "No ChooseWindow/GameplayUiController found in the currently loaded scene(s) - is the gameplay scene open/loaded?");
            return;
        }

        LogHelper.Log("Debug", $"Upgrade screen debug state set (skipAnimation={skipAnimation}, rampIn={rampInDuration}, rampOut={rampOutDuration}) on {windowCount} ChooseWindow(s) and {controllerCount} GameplayUiController(s).");
    }

    private static int ApplyToAll<T>(System.Action<SerializedObject> apply) where T : Object
    {
        var instances = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (T instance in instances)
        {
            Undo.RecordObject(instance, "Set Upgrade Screen Debug State");

            var so = new SerializedObject(instance);
            apply(so);
            so.ApplyModifiedProperties();

            if (Application.isPlaying == false)
                EditorUtility.SetDirty(instance);
        }

        return instances.Length;
    }
}
