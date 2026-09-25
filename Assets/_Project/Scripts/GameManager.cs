using System;
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
