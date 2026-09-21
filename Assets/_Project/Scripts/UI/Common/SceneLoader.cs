using System;
using QuantumUser.View.Util;
using UnityEngine.SceneManagement;

// The one entry point for transitions that should show the generic loading screen. Callers never
// touch LoadingScreen directly, so the fallback below (no prefab -> plain unmasked behaviour) lives
// in exactly one place and a missing/misauthored prefab can never trap the player.
//
//   SceneLoader.Load("MenuScene");                       // single-scene load behind the screen
//   SceneLoader.Cover(() => { /* change happens here */ }, () => /* ...until this is true */ done);
//                                                        // no scene load - e.g. leave match -> menu
//
// Both hold the screen for at least LoadingScreen's minimumDuration (2s by default); pass a value to
// override it for one call. Not for the match-start screen - that is LoadingWindow, in the menu.
public static class SceneLoader
{
    // True while a transition is on screen. Never creates the screen, so it is safe to ask from any
    // hot path - e.g. to debounce a second Leave click, or to tell an already-covered disconnect
    // apart from one that still needs its own cover.
    public static bool IsBusy => LoadingScreen.IsTransitioning;

    public static void Load(string sceneName, float? minimumDuration = null)
    {
        LoadingScreen screen = LoadingScreen.Instance;
        if (screen == null)
        {
            LogHelper.Warn("SceneLoader", $"No loading screen available - loading '{sceneName}' directly.");
            SceneManager.LoadScene(sceneName);
            return;
        }

        screen.BeginSceneLoad(sceneName, minimumDuration);
    }

    // The screen is fully up BEFORE whileCovered runs. holdUntil (optional) keeps it up past the
    // minimum until it returns true, for work the action only starts - see LoadingScreen.BeginCover.
    public static void Cover(Action whileCovered, Func<bool> holdUntil = null, float? minimumDuration = null)
    {
        LoadingScreen screen = LoadingScreen.Instance;
        if (screen == null)
        {
            LogHelper.Warn("SceneLoader", "No loading screen available - running the action directly.");
            whileCovered?.Invoke();
            return;
        }

        screen.BeginCover(whileCovered, holdUntil, minimumDuration);
    }
}
