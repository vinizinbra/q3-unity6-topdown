using System;
using PrimeTween;
using UnityEngine;

public class UISizeDeltaTween : UiTween
{
    private void Reset()
    {
        var t = transform as RectTransform;
        from = t.sizeDelta;
        to = t.sizeDelta;
    }

    public override void SetTo()
    {
        to = (transform as RectTransform).sizeDelta;
    }

    public override void SetFrom()
    {
        from = (transform as RectTransform).sizeDelta;
    }

    public override void Play()
    {
        IsPlaying = true;
        (transform as RectTransform).sizeDelta = from;
        Tween.UISizeDelta(transform as RectTransform, from.XY(), to.XY(), duration,ease,1,CycleMode.Restart,delay).OnComplete(PlayAgainLogic,false);
    }

    public override void PlayBackward(bool playFrom = false)
    {
        if(playType != UiPlayType.ONCE) return;
        IsPlaying = true;
        if(playFrom)
            (transform as RectTransform).sizeDelta = to;
        Tween.UISizeDelta(transform as RectTransform, from.XY(), duration*backwardMultiplier,ReverseEase(),1,CycleMode.Restart,startDelay).OnComplete(PlayAgainLogic,false);

    }
}