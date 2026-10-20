using System.Collections;
using UnityEngine;

/// <summary>
/// Camera/FX choreography for the boss reveal, played inside the sim's existing intro hard pause
/// (RunPhaseUtility.BeginBossEncounter -> Global.BossPauseTimer). BossWidget still owns WHEN it starts (the cut under
/// the black fade, once the boss view exists) and the reveal card; this only adds the impact, so it's the mirror of
/// the defeat cinematic (BossDefeatSequenceDirector), whose Rift Pink blast/crystals/flash it reuses:
///
///   cut      - camera on the boss, pulled out wide
///   slam     - zoom slams in (with a small overshoot), and on impact: white flash, Rift Pink blast + crystal spray at
///              the boss's feet, heavy shake, haptic, the boss's body punches up - and the reveal card bangs in
///   hold     - a slow push-in with a low rumble while the reveal card is up
///   release  - zoom override handed back before the pause ends; BossWidget then eases the camera back to the players
///
/// Self-installed like the defeat director - nothing to place in a scene. Tuning is in the fields below.
/// </summary>
public class BossIntroSequenceDirector : MonoBehaviour
{
    private static BossIntroSequenceDirector instance;

    [Header("Cut")]
    [SerializeField, Tooltip("Camera zoom (multiplier on its offset - higher is further out) at the cut, before the slam.")]
    private float wideZoom = 1.9f;
    [SerializeField, Tooltip("Seconds after the cut before the slam starts - lets the fade-in land first.")]
    private float slamDelay = 0.35f;

    [Header("Slam")]
    [SerializeField] private float slamDuration = 0.16f;
    [SerializeField, Tooltip("Zoom the slam lands on (closer than normal play, which never goes below 1).")]
    private float slamZoom = 0.7f;
    [SerializeField, Tooltip("How far past slamZoom the slam overshoots before settling.")]
    private float slamOvershoot = 0.06f;
    [SerializeField] private float slamFlashAlpha = 0.7f;
    [SerializeField] private float slamFlashFade = 0.4f;
    [SerializeField] private float slamShakeAmplitude = 1f;
    [SerializeField] private float slamShakeDuration = 0.6f;
    [SerializeField] private float slamExplosionScale = 6f;
    [SerializeField] private float slamCrystalScale = 1f;
    [SerializeField, Tooltip("The boss's body scale at the slam, settling back to 1.")]
    private float slamBodyPunch = 1.2f;
    [SerializeField] private float slamBodyPunchDuration = 0.35f;

    [Header("Hold")]
    [SerializeField, Tooltip("Zoom the camera slowly pushes in to while the reveal card is up.")]
    private float holdZoom = 0.62f;
    [SerializeField] private float rumbleInterval = 0.35f;
    [SerializeField] private float rumbleAmplitude = 0.08f;

    [Header("Release")]
    [SerializeField, Tooltip("Seconds before the pause ends that the zoom override is released, so the camera is already easing out when gameplay resumes.")]
    private float releaseLead = 0.7f;

    private Coroutine _sequence;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        if (instance != null)
            return;

        var go = new GameObject(nameof(BossIntroSequenceDirector));
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        instance = go.AddComponent<BossIntroSequenceDirector>();
    }

    // Called by BossWidget at the cut (under the black fade), with the camera already focused on the boss.
    // pauseRemaining = seconds left on the sim's intro pause from this moment. onImpact fires on the slam - BossWidget
    // shows the reveal card there, so the card bangs in WITH the impact instead of hiding the slam behind it. Without
    // a director (or a boss view) it fires immediately, so the card always shows.
    public static void Play(Transform boss, float pauseRemaining, System.Action onImpact = null)
    {
        if (instance == null || boss == null)
        {
            onImpact?.Invoke();
            return;
        }

        if (instance._sequence != null)
            instance.StopCoroutine(instance._sequence);

        instance._sequence = instance.StartCoroutine(instance.Run(boss, pauseRemaining, onImpact));
    }

    private IEnumerator Run(Transform boss, float pauseRemaining, System.Action onImpact)
    {
        FollowCamera camera = FollowCamera.I;
        float start = Time.unscaledTime;
        float releaseAt = Mathf.Max(slamDelay + slamDuration + 0.5f, pauseRemaining - releaseLead);

        camera?.SetZoomOverride(wideZoom);

        while (Time.unscaledTime - start < slamDelay)
            yield return null;

        // Slam: ease-in to just past the target, then settle back onto it.
        float t = 0f;
        while (t < slamDuration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / slamDuration);
            camera?.SetZoomOverride(Mathf.Lerp(wideZoom, slamZoom - slamOvershoot, k * k * k));
            yield return null;
        }

        Impact(boss);
        onImpact?.Invoke();

        t = 0f;
        while (t < 0.12f)
        {
            t += Time.unscaledDeltaTime;
            camera?.SetZoomOverride(Mathf.Lerp(slamZoom - slamOvershoot, slamZoom, Mathf.Clamp01(t / 0.12f)));
            yield return null;
        }

        // Hold: slow push-in with a low rumble.
        float holdStart = Time.unscaledTime;
        float holdLength = Mathf.Max(0.01f, releaseAt - (holdStart - start));
        float nextRumble = 0f;

        while (Time.unscaledTime - start < releaseAt)
        {
            float k = Mathf.Clamp01((Time.unscaledTime - holdStart) / holdLength);
            camera?.SetZoomOverride(Mathf.Lerp(slamZoom, holdZoom, 1f - (1f - k) * (1f - k)));

            if (Time.unscaledTime >= nextRumble)
            {
                nextRumble = Time.unscaledTime + rumbleInterval;
                camera?.Shake(rumbleAmplitude, rumbleInterval, 18f);
            }

            yield return null;
        }

        // Release: the normal eased zoom takes over from here; BossWidget clears the focus when the pause ends.
        camera?.SetZoomOverride(null);
        _sequence = null;
    }

    private void Impact(Transform boss)
    {
        Vector3 feet = boss != null ? boss.position : Vector3.zero;
        BossDefeatSequenceDirector fx = BossDefeatSequenceDirector.Instance;

        fx?.FlashScreen(slamFlashAlpha, slamFlashFade);
        fx?.PlayPinkExplosion(feet, slamExplosionScale);
        fx?.PlayCrystalSparks(feet, slamCrystalScale);
        FollowCamera.I?.Shake(slamShakeAmplitude, slamShakeDuration, 22f);
        Haptics.Play(HapticsConfig.Instance.bossKilled);

        if (boss != null)
            StartCoroutine(PunchBody(boss));
    }

    private IEnumerator PunchBody(Transform boss)
    {
        Vector3 baseScale = boss.localScale;
        float t = 0f;

        while (t < slamBodyPunchDuration && boss != null)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / slamBodyPunchDuration);
            // Damped bounce: pops to slamBodyPunch and wobbles back to rest.
            float wobble = Mathf.Cos(k * Mathf.PI * 3f) * (1f - k);
            boss.localScale = baseScale * (1f + (slamBodyPunch - 1f) * wobble);
            yield return null;
        }

        if (boss != null)
            boss.localScale = baseScale;
    }
}
