using PrimeTween;
using UnityEngine;

// Settings that only matter on a DEVICE build, applied at startup.
//
// A Development Build on Android pays roughly 100+ ms of console work for EVERY Debug.Log / warning
// / error (script stack trace + logcat), and profiled captures showed 39 of them adding up to 5 s of
// hitches in one fight. The Editor is left alone - there the warnings are cheap and worth seeing.
//
// 1. PrimeTween's two chatty warnings. "Tween is started on GameObject that is not active in
//    hierarchy" fires for every flash/pop on a hidden child (HitFeedback.ApplyFlash on an inactive
//    head sprite on every pickup, level-up cards that tween while their popup is closed) and "Tween
//    duration (0) <= 0" for an intentionally instant tween. Both are harmless here: the tween still
//    runs, there is just nothing to warn about.
// 2. The Profiler's own memory cap. The default (128 MB) is hit within a minute of a busy fight, at
//    which point the Profiler stops itself ("Stopping profiler. Profiler is not able to send data
//    ... fast enough") and the capture is silently truncated to its first few dozen frames.
//
// Two separate entry points on purpose. PrimeTween creates its manager in its OWN BeforeSceneLoad
// callback, and Unity gives no ordering between two such callbacks: touching PrimeTweenConfig from
// ours can run first and hit a null manager (it did - a NullReferenceException on device that also
// skipped everything after it). AfterSceneLoad is always later, so the manager exists. The first
// scene's Awake() has already run by then, which only costs a handful of warnings.
internal static class DeviceLogNoise
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RaiseProfilerMemoryCap()
    {
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
        UnityEngine.Profiling.Profiler.maxUsedMemory = 512 * 1024 * 1024;
#endif
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void SilencePrimeTweenWarnings()
    {
#if !UNITY_EDITOR
        PrimeTweenConfig.warnTweenOnDisabledTarget = false;
        PrimeTweenConfig.warnZeroDuration = false;
#endif
    }
}
