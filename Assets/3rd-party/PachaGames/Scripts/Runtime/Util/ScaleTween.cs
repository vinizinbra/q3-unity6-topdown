using System;
using PrimeTween;

public class ScaleTween : UiTween
{
    private void Reset()
    {
        from = transform.localScale;
        to = transform.localScale;
    }

    public override void SetTo()
    {
        to = transform.localScale;
    }

    public override void SetFrom()
    {
        from = transform.localScale;
    }

    public override void Play()
    {
        IsPlaying = true;
        transform.localScale = from;
        Tween.Scale(transform,from, to, duration,ease,1,CycleMode.Restart,delay).OnComplete(PlayAgainLogic,false);
    }

    public override void PlayBackward(bool playFrom = false)
    {
        if(playType != UiPlayType.ONCE) return;
        IsPlaying = true;
        transform.localScale = to;
        Tween.Scale(transform,to, from, duration*backwardMultiplier,ReverseEase(),1,CycleMode.Restart,startDelay).OnComplete(PlayAgainLogic,false);
    }
}