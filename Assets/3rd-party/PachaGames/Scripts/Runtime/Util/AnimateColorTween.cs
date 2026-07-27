using System;
using NaughtyAttributes;
using PrimeTween;
using UnityEngine;

public class AnimateColorTween : MonoBehaviour{

    public Ease ease = Ease.OutBack;
    public Color from;
    public Color to;
    public float duration = 0.5f;
    public float startDelay = 0;
    public float endDelay = 0;
    public bool playOnEnable = true;
    public System.Action onComplete;
    private bool isPlaying;
    public float backwardMultiplier = 0.7f;
    public int cycles = 1;
    public CycleMode cycleMode;
    private UnityEngine.UI.Image target;


    private void Awake()
    {
        target = GetComponent<UnityEngine.UI.Image>();
    }

    private void Reset()
    {
        target = GetComponent<UnityEngine.UI.Image>();
        from = target.color;
        to = target.color;
    }

    private void OnEnable()
    {
        
        if(playOnEnable)
            PlayForward();
    }

    [Button]
    public void PlayForward()
    {
        Tween.Color(target,from,to,new TweenSettings(duration,ease,cycles,cycleMode,startDelay,endDelay) )
            .OnComplete(()=> onComplete?.Invoke());
    }
    [Button]
    public void PlayBackward()
    {
        Tween.Color(target,from,to,new TweenSettings(duration*backwardMultiplier,ease,cycles,cycleMode,startDelay,endDelay) )
            .OnComplete(()=> onComplete?.Invoke());
    }
    [Button]
    public void PlayBackwardFromCurrent()
    {
        Tween.Color(target,from,new TweenSettings(duration*backwardMultiplier,ease,cycles,cycleMode,startDelay,endDelay) )
            .OnComplete(()=> onComplete?.Invoke());
    }

    [Button]
    public void Stop()
    {
        Tween.StopAll(target);
    }
}