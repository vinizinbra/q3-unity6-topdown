using System;
using System.Collections;
using NaughtyAttributes;
using Quantum;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    // The additively loaded gameplay scene (Quantum loads/unloads it with the session). Shared so
    // "has the match scene finished unloading" checks don't each hardcode the string.
    public const string GameplaySceneName = "HeroRoyaleGameplayNewScene";

    public TabGroup bottomMenu;
    public bool isPlayingOffline = false;

    // IntroSequence sets this right before loading MenuScene, and Start consumes it once to drop the
    // player straight into the Training Zone instead of the menu (it has its own Skip button - see
    // TrainingZoneSkipWidget). See docs/training-zone.md.
    private static bool _startTrainingZoneOnLoad;
    public MainMenuTab MainMenuTab;

    [SerializeField, Tooltip("MenuScene's root Canvas - stays loaded (additive gameplay scene load) for the whole session otherwise, so it has to be explicitly disabled once gameplay starts or it keeps rendering/batching underneath the gameplay HUD canvas for nothing.")]
    private Canvas menuCanvas;

    private void Awake()
    {
        Instance = this;
    }

    public TabContent SelectTab<T>() where T : TabContent
    {
        return bottomMenu.SelectTab<T>();
    }
    public TabContent GetTab<T>() where T : TabContent
    {
        return bottomMenu.GetTab<T>();
    }

    public void ShowVictoryScreen(int placement)
    {
        var victoryTab = SelectTab<VictoryTab>() as VictoryTab;
        victoryTab.Setup(placement);
    }

    public int placement = 0;

    [Button]
    public void TestVictoryScreen()
    {
        ShowVictoryScreen(placement);
    }

    private void Start()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;

        if (_startTrainingZoneOnLoad)
        {
            _startTrainingZoneOnLoad = false;
            StartCoroutine(StartTrainingZoneNextFrame());
        }
    }

    public static void RequestTrainingZoneAfterIntro()
    {
        _startTrainingZoneOnLoad = true;
    }

    // Still set during MenuScene's Awakes (Start consumes it) - MenuMusicPlayer reads it there so the
    // intro's music carries on into the Training Zone instead of the menu theme cutting in.
    public static bool IsTrainingZoneAfterIntroPending => _startTrainingZoneOnLoad;

    // True once a match (e.g. the Training Zone) has fully handed off from LoadingWindow to InMatchWindow -
    // IntroSequence holds its loading cover until then.
    public static bool IsInMatchWindowShown()
    {
        if (Instance == null || Instance.MainMenuTab == null || Instance.MainMenuTab.windowManager == null)
            return false;

        return Instance.MainMenuTab.windowManager.currentWindow is InMatchWindow;
    }

    // One frame late so every MenuScene Start (window manager, MatchMakingConfig) has run before
    // StartOfflineSession shows LoadingWindow.
    private IEnumerator StartTrainingZoneNextFrame()
    {
        yield return null;

        PlayTrainingZone();
    }

    private UnityEngine.SceneManagement.Scene _gameplayScene;

    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, LoadSceneMode arg1)
    {
        if (scene.name == GameplaySceneName)
        {
            _gameplayScene = scene;
            SetInGameTab();
        }
    }

    void SetInGameTab()
    {
        // A disconnect can land while the gameplay scene is still loading and already have put the
        // menu back (MatchMakingConfig.ReturnToMenuAfterDisconnect) - hiding the Canvas now would
        // leave that menu invisible.
        if (MainMenuTab != null && MainMenuTab.windowManager != null && MainMenuTab.windowManager.currentWindow is MainMenuWindow)
            return;
        if (menuCanvas != null)
            menuCanvas.enabled = false;
    }

    [Button]
    public void PlayOffline()
    {
        // The gameplay scene is loaded automatically by SessionRunner.StartAsync via the map's
        // own Scene reference (AutoLoadSceneFromMap) - same as the online StartRunner path -
        // so there's no manual SceneManager.LoadSceneAsync call needed here.
        isPlayingOffline = true;
        MatchMakingConfig.Instance.StartOfflineRunner();
    }

    // Offline Training Zone - see MatchMakingConfig.StartTrainingZoneRunner / docs/training-zone.md.
    [Button]
    public void PlayTrainingZone()
    {
        isPlayingOffline = true;
        MatchMakingConfig.Instance.StartTrainingZoneRunner();
    }

    [Button]
    public void UnloadScene()
    {
        /*
        if (_gameplayScene != null)
            SceneManager.UnloadSceneAsync(_gameplayScene);
        var quantumRunner = FindObjectOfType<QuantumRunner>();
        if(quantumRunner != null)
            Destroy(quantumRunner.gameObject);
        SetMenu();
        isPlayingOffline = false;
*/
    }

    private void SetMenu()
    {
        if (menuCanvas != null)
            menuCanvas.enabled = true;
    }
}
