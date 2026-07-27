using System;
using NaughtyAttributes;
using PrimeTween;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;

public abstract class UiTween : MonoBehaviour
{
    public Ease ease = Ease.OutBack;
    public Vector3 from;
    public Vector3 to;
    public float duration = 0.5f;
    public float startDelay = 0;
    [ShowIf("playType",UiPlayType.PINGPONG)]
    public float pingPongDelay = 0;
    public bool playOnEnable = true;
    public UiPlayType playType;
    public Action onComplete;
    private bool isPlaying;
    private bool isPlayingForward = true;
    public float backwardMultiplier = 0.7f;
    
    protected float delay => isPlayingForward? startDelay : pingPongDelay;
    public bool IsPlaying
    {
        get => isPlaying;
        set => isPlaying = value;
    }

    public enum UiPlayType
    {
        ONCE,
        LOOP,
        PINGPONG
    }

    private void OnEnable()
    {
        if (playOnEnable)
        {
            if(!IsPlaying)
                Play();
        }
    }

    [Button]
    public abstract void SetTo();
    [Button]
    public abstract void SetFrom();
    
    [Button]
    public abstract void Play();
    [Button]
    public abstract void PlayBackward(bool playFrom = false);

    public Ease ReverseEase()
    {
        switch (ease)
        {
            case Ease.InSine:
                return Ease.OutSine;
            case Ease.OutSine:
                return Ease.InSine;
            case Ease.InElastic:
                return Ease.OutElastic;
            case Ease.OutElastic:
                return Ease.InElastic;
            case Ease.InBack:
                return Ease.OutBack;
            case Ease.OutBack:
                return Ease.InBack;
            default:
                return ease;
        }
        
    }
    public void PlayAgainLogic()
    {
        if(gameObject ==null) return;
        switch (playType)
        {
            case UiPlayType.ONCE:
                isPlaying = false;
                onComplete?.Invoke();
                break;
            case UiPlayType.LOOP:
                Play();
                break;
            case UiPlayType.PINGPONG:
                isPlayingForward = !isPlayingForward;
                var temp = from;
                from = to;
                to = temp;
                Play();
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}