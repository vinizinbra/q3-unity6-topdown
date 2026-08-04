using System;
using System.Collections;
using System.Collections.Generic;
using Quantum;
using Quantum.Demo;
using UnityEngine;
using UnityEngine.Serialization;
using Button = UnityEngine.UI.Button;

public class MainMenuWindow : UiWindow
{
    [FormerlySerializedAs("matchMakingConfigNew")] public MatchMakingConfig matchMakingConfig;
    public Button reconnectButton;
    public void OpenParty()
    {
        var mainMenuTab = GameManager.Instance.bottomMenu.SelectTab<MainMenuTab>() as MainMenuTab;
        mainMenuTab.windowManager.ShowWindow<LobbyWindow>();
    }

    public override void Show()
    {
        base.Show();
        Application.targetFrameRate = 60;
    }

    void Update()
    {
        reconnectButton.gameObject.SetActive(MatchMakingConfig.Instance.CanReconnect);
    }
    public void QuickPlay()
    {
        MatchMakingConfig.Instance.CleanReconnectConfig();
        MatchMakingConfig.Instance.matchMakingType = MatchMakingConfig.MatchMakingType.QUICKPLAY;
        MatchMakingConfig.Instance.Quickplay();
    }
    public void Reconnect()
    {
        MatchMakingConfig.Instance.matchMakingType = MatchMakingConfig.MatchMakingType.QUICKPLAY;
        MatchMakingConfig.Instance.ReconnectAsync();
    }
    
    public void Party()
    {
        GameManager.Instance.MainMenuTab.windowManager.ShowWindow<LobbyWindow>();
    }
    public void Practice()
    {
        GameManager.Instance.PlayOffline();

    }
}
