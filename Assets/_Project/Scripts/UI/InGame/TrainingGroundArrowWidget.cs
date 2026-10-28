using UnityEngine;

// "This way" arrow lying on the Training Zone floor (added by TrainingZoneBuilder: a flat SpriteRenderer, Rift Pink)
// - see docs/training-zone.md. Nudges forward along its own right axis and eases back on a loop, unscaled like the
// guide's pop. TrainingZoneGuideWidget pops it in with its station's text.
public class TrainingGroundArrowWidget : MonoBehaviour
{
    [Tooltip("How far the arrow nudges forward, in world units.")]
    [SerializeField] private float nudgeDistance = 0.35f;
    [SerializeField] private float nudgePeriod = 1.1f;

    private Vector3 _home;

    private void Start()
    {
        _home = transform.localPosition;
    }

    private void Update()
    {
        float phase = Time.unscaledTime / nudgePeriod % 1f;
        // Quick push out, slow ease back.
        float push = phase < 0.3f ? Mathf.SmoothStep(0f, 1f, phase / 0.3f) : Mathf.SmoothStep(1f, 0f, (phase - 0.3f) / 0.7f);
        transform.localPosition = _home + transform.parent.InverseTransformDirection(transform.right) * (push * nudgeDistance);
    }
}
