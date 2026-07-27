using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class VictoryWindow : UiWindow
{
    public TMP_Text placement;
    public Animator animator;
    public void Setup(int playersAlive)
    {
        placement.text = "";// playersAlive.GetOrdinalString();
    }

}