using PrimeTween;
using UnityEngine;

public class OtherTween : UiTween
{
    public CycleMode tweenCycleMode;
    public int cycles =1;
    public float  endDelay =1;
    public override void SetTo()
    {
        
    }

    public override void SetFrom()
    {
    }

    public override void Play()
    {
        Tween.Scale(transform, Vector3.zero, Vector3.one, duration, ease, cycles, tweenCycleMode, startDelay, endDelay);
    }

    public override void PlayBackward(bool playFrom = false)
    {
    }
}