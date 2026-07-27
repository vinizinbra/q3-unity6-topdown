using System.Collections;
using System.Threading.Tasks;
using UnityEngine;

public static class Awaiters
{
    public static Task WaitOneFrame() =>
        RunCoroutine(WaitOneFrameRoutine());

    public static Task WaitForSeconds(float seconds) =>
        RunCoroutine(WaitForSecondsRoutine(seconds));

    public static Task WaitForSecondsRealtime(float seconds) =>
        RunCoroutine(WaitForSecondsRealtimeRoutine(seconds));

    public static Task WaitUntil(System.Func<bool> condition) =>
        RunCoroutine(new WaitUntil(condition));

    public static Task WaitWhile(System.Func<bool> condition) =>
        RunCoroutine(new WaitWhile(condition));

    public static Task WaitForEndOfFrame() =>
        RunCoroutine(new WaitForEndOfFrame());

    // ---- Private helpers ----

    static async Task RunCoroutine(IEnumerator routine)
    {
        var tcs = new TaskCompletionSource<bool>();
        CoroutineRunner.Instance.StartCoroutine(Run(routine, tcs));
        await tcs.Task;
    }

    static async Task RunCoroutine(YieldInstruction instruction)
    {
        var tcs = new TaskCompletionSource<bool>();
        CoroutineRunner.Instance.StartCoroutine(Run(instruction, tcs));
        await tcs.Task;
    }

    static IEnumerator Run(IEnumerator routine, TaskCompletionSource<bool> tcs)
    {
        yield return routine;
        tcs.TrySetResult(true);
    }

    static IEnumerator Run(YieldInstruction instruction, TaskCompletionSource<bool> tcs)
    {
        yield return instruction;
        tcs.TrySetResult(true);
    }

    static IEnumerator WaitOneFrameRoutine()
    {
        yield return null;
    }

    static IEnumerator WaitForSecondsRoutine(float seconds)
    {
        yield return new WaitForSeconds(seconds);
    }

    static IEnumerator WaitForSecondsRealtimeRoutine(float seconds)
    {
        yield return new WaitForSecondsRealtime(seconds);
    }

    // ---- Coroutine runner ----
    class CoroutineRunner : MonoBehaviour
    {
        static CoroutineRunner _instance;
        public static CoroutineRunner Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("Awaiters");
                    Object.DontDestroyOnLoad(go);
                    _instance = go.AddComponent<CoroutineRunner>();
                }
                return _instance;
            }
        }
    }
}
