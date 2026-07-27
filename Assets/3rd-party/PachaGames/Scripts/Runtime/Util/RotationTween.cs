using System;
using UnityEngine;
using System.Numerics;
using PrimeTween;
using Quaternion = UnityEngine.Quaternion;

public class RotationTween : UiTween
{
    private void Reset()
    {
        from = transform.localRotation.eulerAngles;
        to = transform.localRotation.eulerAngles;
    }

    public override void SetTo()
    {
        to = transform.localRotation.eulerAngles;
    }

    public override void SetFrom()
    {
        from = transform.localRotation.eulerAngles;
    }

    public override void Play()
    {
        IsPlaying = true;
        transform.localRotation = Quaternion.Euler(from);
        Tween.LocalRotation(transform,from, to, duration,ease,1,CycleMode.Restart,delay).OnComplete(PlayAgainLogic,false);
    }

    public override void PlayBackward(bool playFrom = false)
    {
        if(playType != UiPlayType.ONCE) return;
        IsPlaying = true;
        transform.localRotation = Quaternion.Euler(to);
        Tween.LocalRotation(transform,to, from, duration*backwardMultiplier,ReverseEase(),1,CycleMode.Restart,startDelay).OnComplete(PlayAgainLogic,false);
    }
}