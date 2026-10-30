using UnityEngine;

/// <summary>
/// The 3D ice spike of an eruption burst (the Frost Patriarch's Icebreaker): lives on the eruption effect prefab that
/// EffectsManager plays from AreaDetonated (AreaHitData.BlastEffectPrefab), so it appears exactly where and when the
/// simulation's eruption hits. One of <see cref="variants"/> (Arctic tileset spike models) punches up out of the floor
/// with an overshoot, holds, then shatters (<see cref="shatter"/>) and sinks back - plus a camera shake on the hit.
/// Purely visual; the root's particles (snow burst + lingering frost mist) keep the pooled instance alive for the whole
/// animation (EffectsManager releases it once nothing is alive).
///
/// Armed in OnEnable, started on the next Update - EffectsManager prewarms its pools by activating and releasing
/// instances in the same frame, which must stay silent (no shake, no spike) - same reason as PlaySoundOnEnable.
/// </summary>
public class IceEruptionSpikeView : MonoBehaviour
{
    [SerializeField, Tooltip("Spike models, children of this object; one is shown per eruption.")]
    private Transform[] variants = System.Array.Empty<Transform>();
    [SerializeField, Tooltip("Shard burst emitted (Emit, not Play - its own emission is off so the burst's Play at spawn never fires it) when the spike shatters.")]
    private ParticleSystem shatter;
    [SerializeField] private int shatterCount = 22;

    [SerializeField, Tooltip("How far below the floor (local units) the spike starts.")] private float sinkDepth = 1.2f;
    [SerializeField] private float riseTime = 0.08f;
    [SerializeField] private float overshoot = 1.2f;
    [SerializeField] private float settleTime = 0.12f;
    [SerializeField] private float holdTime = 0.6f;
    [SerializeField] private float shatterTime = 0.25f;
    [SerializeField] private float shakeImpact = 1.2f;

    private Transform _spike;
    private Vector3 _baseScale;
    private Vector3[] _authoredScales;
    private float _elapsed;
    private bool _armed;
    private bool _running;
    private bool _shattered;

    private void Awake()
    {
        _authoredScales = new Vector3[variants.Length];
        for (int i = 0; i < variants.Length; i++)
            _authoredScales[i] = variants[i] != null ? variants[i].localScale : Vector3.one;
    }

    private void OnEnable()
    {
        _armed = true;
        _running = false;
        SetVisible(null);
    }

    private void OnDisable()
    {
        _armed = false;
        _running = false;
        SetVisible(null);
    }

    private void Update()
    {
        if (_armed == true)
        {
            _armed = false;
            Begin();
        }

        if (_running == false || _spike == null)
            return;

        _elapsed += Time.deltaTime;
        Apply(_elapsed);
    }

    // Editor preview (Recordings / look-dev): shows the first variant at a given time of the animation, no shake.
    public void PreviewAt(float time)
    {
        if (variants.Length == 0)
            return;

        if (_authoredScales == null)
            Awake();

        _spike = variants[0];
        _baseScale = _authoredScales[0];
        SetVisible(_spike);
        _shattered = time > riseTime + settleTime + holdTime + 0.0001f;
        Apply(time);
    }

    private void Apply(float elapsed)
    {
        float scale;
        float height;

        if (elapsed < riseTime)
        {
            float t = elapsed / riseTime;
            height = Mathf.Lerp(-sinkDepth, 0f, 1f - (1f - t) * (1f - t));
            scale = Mathf.Lerp(0.4f, overshoot, t);
        }
        else if (elapsed < riseTime + settleTime)
        {
            float t = (elapsed - riseTime) / settleTime;
            height = 0f;
            scale = Mathf.Lerp(overshoot, 1f, t);
        }
        else if (elapsed < riseTime + settleTime + holdTime)
        {
            height = 0f;
            scale = 1f;
        }
        else
        {
            if (_shattered == false)
            {
                _shattered = true;
                if (shatter != null)
                    shatter.Emit(shatterCount);
            }

            float t = Mathf.Clamp01((elapsed - riseTime - settleTime - holdTime) / shatterTime);
            height = Mathf.Lerp(0f, -sinkDepth * 0.6f, t * t);
            scale = Mathf.Lerp(1f, 0f, t * t);

            if (t >= 1f)
            {
                _running = false;
                SetVisible(null);
                return;
            }
        }

        _spike.localPosition = new Vector3(0f, height, 0f);
        _spike.localScale = new Vector3(_baseScale.x * scale, _baseScale.y * Mathf.Max(scale, 0.05f), _baseScale.z * scale);
    }

    private void Begin()
    {
        if (variants.Length == 0)
            return;

        int index = Random.Range(0, variants.Length);
        _spike = variants[index];
        _baseScale = _authoredScales[index];
        SetVisible(_spike);

        // Upright and randomly turned whatever rotation the burst prefab itself carries.
        _spike.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        _elapsed = 0f;
        _shattered = false;
        _running = true;

        FollowCamera.I?.ShakeAtPosition(transform.position, shakeImpact);
    }

    private void SetVisible(Transform shown)
    {
        foreach (Transform variant in variants)
        {
            if (variant != null)
                variant.gameObject.SetActive(variant == shown);
        }
    }
}
