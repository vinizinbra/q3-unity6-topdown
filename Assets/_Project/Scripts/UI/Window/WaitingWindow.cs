using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class WaitingWindow : UiWindow
{
    [SerializeField] private Slider matchBeginsSlider;
    [SerializeField] private TMP_Text matchStartingNumber;
    private int currentSeconds = 0;

    private int CurrentSeconds
    {
        get => currentSeconds;
        set
        {
            if(currentSeconds == value)
                return;
            //scaleTween.PlayAtTime(0);
            
            currentSeconds = value;
            matchStartingNumber.text = (currentSeconds+1).ToString();
        }
    }
    public void Setup(float timeToStart, int seconds)
    {
        matchBeginsSlider.value = timeToStart;
        CurrentSeconds = seconds;

    }
   
    
}