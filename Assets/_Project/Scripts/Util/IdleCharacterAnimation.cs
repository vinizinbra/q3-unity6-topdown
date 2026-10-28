using UnityEngine;

// Idle "alive" loop for a flat character cut-out (e.g. the Store's merchant sprite): a breathing
// squash-and-stretch, a slow side-to-side sway, and every few seconds a quick little hop. Expects
// the sprite's pivot at its base so the scale and sway grow out of where the character stands
// instead of around its middle. Same rule as IdleFloatAnimation: attach it to a child VISUAL, never
// to an entity's own root - a Quantum entity view rewrites its root transform every frame.
// Phase and hop timing are randomized per instance so several copies don't move in lockstep.
public class IdleCharacterAnimation : MonoBehaviour
{
    [Header("Breathe")]
    [SerializeField, Tooltip("Breaths per second.")]
    private float breatheFrequency = 0.6f;

    [SerializeField, Tooltip("Peak vertical stretch (0.03 = 103% tall); width counter-squashes by half of it so volume reads as preserved.")]
    private float breatheAmount = 0.03f;

    [Header("Sway")]
    [SerializeField, Tooltip("Full left-right cycles per second.")]
    private float swayFrequency = 0.25f;

    [SerializeField, Tooltip("Peak tilt in degrees around the base.")]
    private float swayAngle = 2f;

    [Header("Hop")]
    [SerializeField, Tooltip("Seconds between hops, picked at random in this range. (0, 0) = never hop.")]
    private Vector2 hopInterval = new Vector2(3f, 6f);

    [SerializeField, Tooltip("How long one hop takes, in seconds.")]
    private float hopDuration = 0.35f;

    [SerializeField, Tooltip("Peak hop height, in local units.")]
    private float hopHeight = 0.08f;

    [SerializeField, Tooltip("Extra stretch at the top of the hop and squash on landing.")]
    private float hopSquash = 0.08f;

    private Vector3 _restLocalPosition;
    private Vector3 _restLocalScale;
    private Quaternion _restLocalRotation;
    private float _phase;
    private float _nextHopTime;
    private float _hopStartTime = float.NegativeInfinity;

    private void OnEnable()
    {
        _restLocalPosition = transform.localPosition;
        _restLocalScale = transform.localScale;
        _restLocalRotation = transform.localRotation;
        _phase = Random.value * Mathf.PI * 2f;
        ScheduleHop(Time.time);
    }

    private void OnDisable()
    {
        transform.localPosition = _restLocalPosition;
        transform.localScale = _restLocalScale;
        transform.localRotation = _restLocalRotation;
    }

    private void Update()
    {
        float time = Time.time;

        if (hopInterval.y > 0f && time >= _nextHopTime)
        {
            _hopStartTime = time;
            ScheduleHop(time);
        }

        float breathe = Mathf.Sin(time * breatheFrequency * Mathf.PI * 2f + _phase) * breatheAmount;
        float sway = Mathf.Sin(time * swayFrequency * Mathf.PI * 2f + _phase * 0.5f) * swayAngle;

        // 0..1 through the hop: rise and fall on a sine, stretched in the air, squashed on landing.
        float hopT = (time - _hopStartTime) / hopDuration;
        float lift = 0f;
        float stretch = 0f;

        if (hopT >= 0f && hopT < 1f)
        {
            lift = Mathf.Sin(hopT * Mathf.PI) * hopHeight;
            stretch = hopT < 0.75f ? Mathf.Sin(hopT / 0.75f * Mathf.PI) * hopSquash : -Mathf.Sin((hopT - 0.75f) / 0.25f * Mathf.PI) * hopSquash;
        }

        float stretchY = 1f + breathe + stretch;
        float stretchX = 1f - (breathe + stretch) * 0.5f;

        transform.localPosition = _restLocalPosition + _restLocalRotation * Vector3.up * lift;
        transform.localRotation = _restLocalRotation * Quaternion.Euler(0f, 0f, sway);
        transform.localScale = Vector3.Scale(_restLocalScale, new Vector3(stretchX, stretchY, 1f));
    }

    private void ScheduleHop(float now)
    {
        _nextHopTime = hopInterval.y > 0f ? now + Random.Range(hopInterval.x, hopInterval.y) : float.PositiveInfinity;
    }
}
