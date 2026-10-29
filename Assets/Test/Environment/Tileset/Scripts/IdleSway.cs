using UnityEngine;

// View-only idle motion for small festive props (Carnival balloons): a gentle two-axis sway about the prop's pivot (its
// base). Per-position phase so neighbours never move in lockstep; skipped off screen.
public class IdleSway : MonoBehaviour
{
    [SerializeField, Tooltip("Max tilt (degrees) about the two horizontal axes.")]
    private float swayDegrees = 4f;

    [SerializeField]
    private float swaySpeed = 1.1f;

    private Quaternion baseRotation;
    private Renderer firstRenderer;
    private float phase;

    private void Start()
    {
        baseRotation = transform.localRotation;
        firstRenderer = GetComponentInChildren<Renderer>();
        var p = transform.position;
        phase = Mathf.Repeat(p.x * 0.913f + p.z * 1.517f, 30f);
    }

    private void Update()
    {
        if (firstRenderer != null && !firstRenderer.isVisible)
            return;
        var t = Time.time * swaySpeed + phase;
        var x = Mathf.Sin(t) * swayDegrees;
        var z = Mathf.Sin(t * 0.73f + 1.3f) * swayDegrees * 0.8f;
        transform.localRotation = baseRotation * Quaternion.Euler(x, 0f, z);
    }
}
