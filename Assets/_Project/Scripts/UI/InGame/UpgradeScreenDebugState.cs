// Runtime counterpart to the Editor-only UpgradeScreenDebugToolbar (Assets/_Project/Editor/) - that
// toolbar flips ChooseWindow.debugSkipIntroAnimation and GameplayUiController's upgrade time-scale
// ramp durations via SerializedObject on every instance currently in the loaded scene(s), which only
// works in the Editor. This is one static flag both ChooseWindow.Show() and GameplayUiController's
// ramp-in/ramp-out read directly instead, so a runtime toggle (CheatMenu, gated by CHEATS_ENABLED)
// can skip the upgrade screen's intro animation + Time.timeScale ramp in a build too, without a scene
// reference to either and without needing Editor-only APIs.
public static class UpgradeScreenDebugState
{
    public static bool SkipAnimations;
}
