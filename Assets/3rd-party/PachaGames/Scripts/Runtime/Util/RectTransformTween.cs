using System;
using PrimeTween;
using UnityEngine;

public class RectTransformTween : UiTween
{
    private RectTransform _rectTransform;
    private void Awake()
    {
        _rectTransform = transform as RectTransform;
    }

    private void Reset()
    {
        if(_rectTransform == null)
            _rectTransform = transform as RectTransform;
        from = _rectTransform.anchoredPosition;
        to = _rectTransform.anchoredPosition;
        
    }
    
    public override void SetTo()
    {
        if(_rectTransform == null)
            _rectTransform = transform as RectTransform;
        to = _rectTransform.anchoredPosition;
    }

    public override void SetFrom()
    {
        if(_rectTransform == null)
            _rectTransform = transform as RectTransform;
        from = _rectTransform.anchoredPosition;  
    }

    public override void Play()
    {
        if(_rectTransform == null)
            _rectTransform = transform as RectTransform;
        _rectTransform.anchoredPosition = from;
        IsPlaying = true;
        Tween.UIAnchoredPosition(_rectTransform,from, to, duration,ease,1,CycleMode.Restart,delay).OnComplete(PlayAgainLogic,false);
    }

    public override void PlayBackward(bool playFrom = false)
    {
        if(playType != UiPlayType.ONCE) return;
        if(_rectTransform == null)
            _rectTransform = transform as RectTransform;
        _rectTransform.anchoredPosition = to;
        IsPlaying = true;
        Tween.UIAnchoredPosition(_rectTransform,to, from, duration*backwardMultiplier,ReverseEase(),1,CycleMode.Restart,startDelay).OnComplete(PlayAgainLogic,false);
    }    
}