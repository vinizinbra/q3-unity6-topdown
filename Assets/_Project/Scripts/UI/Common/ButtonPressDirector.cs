using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Gives every UI press (UiPressDetector: Button, Toggle, tabs, cards... by click, tap or Submit) a small squash-and-
/// spring on the pressed object's scale. Self-installed at startup, so there is nothing to place on a button.
///
/// The press is a multiplier layered on top of whatever else drives that Transform's scale (SelectableScaleWidget's
/// select grow, ShakeGrowImpactAnimation, ScaleTween, an Animator): each LateUpdate it divides its own last factor
/// back out, or - if something else wrote a new scale since - takes that as the new base. So it never fights them
/// and always ends exactly on their scale. Runs on unscaled time, since menus/upgrade screens pause the sim.
/// </summary>
public class ButtonPressDirector : MonoBehaviour
{
    // Squash down quickly, then spring back with a small overshoot.
    private const float PressedScale = 0.9f;
    private const float PressDuration = 0.06f;
    private const float ReleaseDuration = 0.26f;
    private const float ReleaseOvershoot = 2.2f;

    private static ButtonPressDirector instance;

    private readonly List<Press> presses = new();

    private class Press
    {
        public Transform Target;
        public float Time;
        public float AppliedFactor = 1f;
        public Vector3 WrittenScale;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        if (instance != null)
            return;

        var go = new GameObject(nameof(ButtonPressDirector));
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        instance = go.AddComponent<ButtonPressDirector>();
    }

    private void OnEnable() => UiPressDetector.Pressed += OnPressed;

    private void OnDisable() => UiPressDetector.Pressed -= OnPressed;

    private void OnPressed(GameObject target)
    {
        // A submit can land after the target already hid itself (an upgrade card disables on pick) - nothing to show.
        if (target.activeInHierarchy == false)
            return;

        foreach (Press press in presses)
        {
            if (press.Target == target.transform)
            {
                press.Time = 0f;
                return;
            }
        }

        presses.Add(new Press { Target = target.transform, WrittenScale = target.transform.localScale });
    }

    private void LateUpdate()
    {
        for (int i = presses.Count - 1; i >= 0; i--)
        {
            Press press = presses[i];
            Transform target = press.Target;

            if (target == null || target.gameObject.activeInHierarchy == false)
            {
                if (target != null)
                    target.localScale = GetBaseScale(press);

                presses.RemoveAt(i);
                continue;
            }

            press.Time += Time.unscaledDeltaTime;
            float factor = Evaluate(press.Time);

            target.localScale = GetBaseScale(press) * factor;
            press.WrittenScale = target.localScale;
            press.AppliedFactor = factor;

            if (press.Time >= PressDuration + ReleaseDuration)
                presses.RemoveAt(i);
        }
    }

    // Untouched since our last write: strip our factor back out. Otherwise another animation set it this frame,
    // and that value is the base.
    private static Vector3 GetBaseScale(Press press)
    {
        Vector3 scale = press.Target.localScale;
        return scale == press.WrittenScale ? scale / press.AppliedFactor : scale;
    }

    private static float Evaluate(float time)
    {
        if (time < PressDuration)
        {
            float t = time / PressDuration;
            return Mathf.LerpUnclamped(1f, PressedScale, 1f - (1f - t) * (1f - t));
        }

        float r = Mathf.Clamp01((time - PressDuration) / ReleaseDuration);
        return Mathf.LerpUnclamped(PressedScale, 1f, EaseOutBack(r));
    }

    private static float EaseOutBack(float t)
    {
        float c = ReleaseOvershoot;
        float p = t - 1f;
        return 1f + (c + 1f) * p * p * p + c * p * p;
    }
}
