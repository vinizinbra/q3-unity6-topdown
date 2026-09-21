using System;
using System.Collections;
using QuantumUser.View.Util;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Generic full-screen loading screen for transitions the game owns end to end: single-scene loads
// (Intro -> Menu) and "cover this action" transitions (leaving a match back to the menu, where the
// menu scene never unloads and there is no scene load at all). Not the match-start screen - that one
// is LoadingWindow, which lives in the menu on purpose (see docs/loading-screen.md).
//
// SELF-INSTANTIATED, not placed in any scene. The first request Instantiates the prefab from
// Resources/LoadingScreen and marks it DontDestroyOnLoad, so it survives the scene swap it is
// covering and works from ANY scene (including pressing Play from a random scene in the Editor) with
// zero per-scene setup. Callers go through SceneLoader; nothing should reference this class directly
// except that facade.
//
// Every timing here is unscaled: a transition can start while Time.timeScale is ramped down (the
// Level-Up screen does that), and that must not stretch or freeze the screen.
//
// Registry-of-one instead of a plain static field for the same reason ToastManager/AnnouncerManager
// use one: a stale static would point at a destroyed object after a domain-reload-less Play Mode exit.
public class LoadingScreen : MonoBehaviour
{
    private const string ResourcePath = "LoadingScreen";

    // Scene loads report progress only up to 0.9 while allowSceneActivation is held false.
    private const float ActivationProgress = 0.9f;

    // The new scene's Awake/Start (or a menu being rebuilt behind a Cover) can hitch the first frames
    // after the swap for a good fraction of a second. Time.unscaledDeltaTime is the RAW length of the
    // previous frame, so one such frame used to eat the entire fade-out in a single step - the screen
    // just vanished instead of fading. Two defences:
    //   - a fade advances by at most MaxFadeStep per frame, and
    //   - the fade-out only starts once CalmFramesNeeded frames in a row were faster than CalmFrameTime
    //     (or SettleTimeout has passed, so a permanently slow device still gets its fade).
    private const float MaxFadeStep = 1f / 20f;
    private const float CalmFrameTime = 1f / 20f;
    private const int CalmFramesNeeded = 3;
    private const float SettleTimeout = 2f;

    private static LoadingScreen _instance;

    // Always a REAL null when the prefab is missing - never a destroyed object - so callers can
    // simply null-check and fall back to an unmasked load.
    public static LoadingScreen Instance
    {
        get
        {
            if (_instance != null)
                return _instance;

            LoadingScreen prefab = Resources.Load<LoadingScreen>(ResourcePath);
            if (prefab == null)
            {
                LogHelper.Error("LoadingScreen", $"No prefab at Resources/{ResourcePath} - transitions will run without a loading screen.");
                return null;
            }

            _instance = Instantiate(prefab);
            _instance.name = prefab.name;
            DontDestroyOnLoad(_instance.gameObject);
            return _instance;
        }
    }

    // True while a transition is running. Static and instance-free on purpose: callers ask this from
    // hot paths (a disconnect callback) that must not spawn the prefab just to learn it is idle.
    public static bool IsTransitioning => _instance != null && _instance.IsBusy;

    // Statics survive a Play Mode exit when Enter Play Mode Options disables domain reload.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _instance = null;
    }

    [Header("Refs")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField, Tooltip("Optional. Filled 0-1 with the load's progress (blended with the minimum-time countdown so it never sits at 100% while still waiting).")]
    private Image progressFill;
    [SerializeField, Tooltip("Optional. Shows the same progress as a percentage, e.g. '73%'.")]
    private TMP_Text progressLabel;

    [Header("Timing")]
    [SerializeField, Min(0f), Tooltip("The screen never disappears sooner than this many seconds after it STARTS fading in, however fast the load is. A per-call value can override it.")]
    private float minimumDuration = 2f;
    [SerializeField, Min(0f), Tooltip("Cover() can be told to keep the screen up until some condition holds (e.g. the gameplay scene has finished unloading). If it never does, the screen lifts anyway after this many seconds - a stuck condition can't trap the player behind it.")]
    private float maximumHoldDuration = 10f;
    [SerializeField, Min(0f)] private float fadeInDuration = 0.3f;
    [SerializeField, Min(0f)] private float fadeOutDuration = 0.4f;

    // True from the first frame of a transition until the screen has fully faded away - callers
    // use it to ignore a second request instead of stacking two transitions.
    public bool IsBusy { get; private set; }

    // Loads a scene (single mode) behind the screen: fade in, load in the background, then swap
    // scenes only once BOTH the load is ready and the minimum time has passed, then fade out.
    public void BeginSceneLoad(string sceneName, float? minimumOverride)
    {
        if (IsBusy == true)
        {
            LogHelper.Warn("LoadingScreen", $"Already busy - ignoring load request for '{sceneName}'.");
            return;
        }

        Activate();
        StartCoroutine(SceneLoadRoutine(sceneName, minimumOverride ?? minimumDuration));
    }

    // Covers an action that isn't a scene load: fade in, run whileCovered on the first fully
    // covered frame, hold until the minimum time has passed AND holdUntil (if any) is true, then
    // fade out. whileCovered is where the actual change happens (shut the runner down, show the
    // menu...), so the screen is always up BEFORE it starts and nothing of it is ever visible
    // half-done; holdUntil keeps the screen up for work that outlives the action itself (an async
    // scene unload the action merely kicks off).
    public void BeginCover(Action whileCovered, Func<bool> holdUntil, float? minimumOverride)
    {
        if (IsBusy == true)
        {
            LogHelper.Warn("LoadingScreen", "Already busy - running the covered action directly (the running transition is already covering it).");
            whileCovered?.Invoke();
            return;
        }

        Activate();
        StartCoroutine(CoverRoutine(whileCovered, holdUntil, minimumOverride ?? minimumDuration));
    }

    // The prefab is saved inactive so it costs nothing while idle; it has to be active before a
    // coroutine can start on it.
    private void Activate()
    {
        IsBusy = true;
        gameObject.SetActive(true);
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = true;
        canvasGroup.interactable = false;
        SetProgress(0f);
    }

    private void Deactivate()
    {
        canvasGroup.blocksRaycasts = false;
        gameObject.SetActive(false);
        IsBusy = false;
    }

    private IEnumerator SceneLoadRoutine(string sceneName, float minimum)
    {
        float start = Time.unscaledTime;
        yield return FadeTo(1f, fadeInDuration);

        AsyncOperation load = SceneManager.LoadSceneAsync(sceneName);
        if (load == null)
        {
            // Not in Build Settings / bad name. Don't strand the player behind the screen.
            LogHelper.Error("LoadingScreen", $"Could not start loading scene '{sceneName}' - is it in Build Settings?");
            yield return FadeTo(0f, fadeOutDuration);
            Deactivate();
            yield break;
        }

        // Hold the swap until the scene is ready AND the minimum has elapsed.
        load.allowSceneActivation = false;
        while (load.progress < ActivationProgress || Time.unscaledTime - start < minimum)
        {
            float loaded = Mathf.Clamp01(load.progress / ActivationProgress);
            float waited = minimum > 0f ? Mathf.Clamp01((Time.unscaledTime - start) / minimum) : 1f;
            SetProgress(Mathf.Min(loaded, waited));
            yield return null;
        }

        SetProgress(1f);
        load.allowSceneActivation = true;
        while (load.isDone == false)
            yield return null;

        // Let the new scene's Awake/Start (and its first layout passes) finish hitching before the
        // screen starts to lift - see MaxFadeStep.
        yield return WaitForSettledFrames();
        yield return FadeTo(0f, fadeOutDuration);
        Deactivate();
    }

    private IEnumerator CoverRoutine(Action whileCovered, Func<bool> holdUntil, float minimum)
    {
        float start = Time.unscaledTime;
        yield return FadeTo(1f, fadeInDuration);

        // A throwing callback must not leave the screen up forever.
        try
        {
            whileCovered?.Invoke();
        }
        catch (Exception e)
        {
            LogHelper.Error("LoadingScreen", $"Covered action threw - lifting the screen anyway: {e}");
        }

        while (Time.unscaledTime - start < minimum)
        {
            SetProgress(minimum > 0f ? Mathf.Clamp01((Time.unscaledTime - start) / minimum) : 1f);
            yield return null;
        }

        // Minimum has passed - now wait for the caller's condition, bounded by the failsafe.
        float holdStart = Time.unscaledTime;
        while (ConditionMet(holdUntil) == false)
        {
            if (Time.unscaledTime - holdStart >= maximumHoldDuration)
            {
                LogHelper.Warn("LoadingScreen", $"Hold condition still false after {maximumHoldDuration:F0}s - lifting the screen anyway.");
                break;
            }

            yield return null;
        }

        SetProgress(1f);

        // The menu behind the screen has just been shown/rebuilt and the gameplay scene torn down -
        // same hitch risk as after a scene swap.
        yield return WaitForSettledFrames();
        yield return FadeTo(0f, fadeOutDuration);
        Deactivate();
    }

    // Waits until CalmFramesNeeded consecutive frames were quick, so the fade that follows is not
    // played across a hitch. Bounded by SettleTimeout. Logs what it saw, which is how to tell a
    // hitch from some other reason for a missing fade.
    private IEnumerator WaitForSettledFrames()
    {
        float startedAt = Time.unscaledTime;
        float worst = 0f;
        int calm = 0;
        int frames = 0;

        while (calm < CalmFramesNeeded && Time.unscaledTime - startedAt < SettleTimeout)
        {
            yield return null;

            float frame = Time.unscaledDeltaTime;
            worst = Mathf.Max(worst, frame);
            calm = frame <= CalmFrameTime ? calm + 1 : 0;
            frames++;
        }

        LogHelper.Log("LoadingScreen", $"Fade-out starting after {frames} frame(s) / {Time.unscaledTime - startedAt:F2}s of settling (slowest frame {worst * 1000f:F0}ms).");
    }

    // No condition = met. A throwing condition counts as met, so a bug in it can't strand the screen.
    private static bool ConditionMet(Func<bool> condition)
    {
        if (condition == null)
            return true;

        try
        {
            return condition();
        }
        catch (Exception e)
        {
            LogHelper.Error("LoadingScreen", $"Hold condition threw - treating it as met: {e}");
            return true;
        }
    }

    private IEnumerator FadeTo(float target, float duration)
    {
        if (duration <= 0f)
        {
            canvasGroup.alpha = target;
            yield break;
        }

        float from = canvasGroup.alpha;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Mathf.Min(Time.unscaledDeltaTime, MaxFadeStep);
            canvasGroup.alpha = Mathf.Lerp(from, target, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }

        canvasGroup.alpha = target;
    }

    private void SetProgress(float value)
    {
        if (progressFill != null)
            progressFill.fillAmount = value;

        if (progressLabel != null)
            progressLabel.text = $"{Mathf.RoundToInt(value * 100f)}%";
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }
}
