using System;
using UnityEngine;

public abstract class UiWindowBase : MonoBehaviour
{
    public abstract void Awake();

    public abstract void Show();
    public abstract void Hide();
}