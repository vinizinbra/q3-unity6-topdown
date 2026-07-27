using System;
using UnityEngine;

public class UiButtonSound : MonoBehaviour
{
    public SoundName uiButtonSound;
    public enum SoundName
    {
        ClickButton,
        BackButton,
        CancelButton
    }

    private void Awake()
    {
        var button = GetComponent<UnityEngine.UI.Button>();
        if(button != null)
            button.onClick.AddListener(Play);
    }

    public void Play()
    {
        Debug.Log(uiButtonSound.ToString());
        AudioController.Play(uiButtonSound.ToString());
    }
}
