using System;
using NaughtyAttributes;
using Quantum;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;
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
        if (scene.name == "HeroRoyaleGameplayNewScene")
        {
            _gameplayScene = scene;
            SetInGameTab();
        }
    }

    void SetInGameTab()
    {
        if (menuCanvas != null)
            menuCanvas.enabled = false;
    }

    [Button]
    public void PlayOffline()
    {
        isPlayingOffline = true;
        SceneManager.LoadSceneAsync("Scenes/HeroRoyaleGameplayNewScene", LoadSceneMode.Additive);
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
