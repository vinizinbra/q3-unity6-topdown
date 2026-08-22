using System;
using System.Collections.Generic;
using Quantum;
using Quantum.Demo;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GameplayWindow : UiWindow
{
    [SerializeField] private Transform playerUiWidgetParent;
    [SerializeField] private TMP_Text eliminateText;

    private void Awake()
    {
        Application.targetFrameRate = 120;
    }


    public void Leave()
    {
        PhotonMain.Disconnect();
    }


}
