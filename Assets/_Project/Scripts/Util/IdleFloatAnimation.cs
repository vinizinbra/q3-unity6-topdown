using UnityEngine;

// Simple idle "floating" animation: bobs this Transform's local Y on a sine wave around wherever it
// was when enabled. Attach it to a child VISUAL (sprite/mesh root), not to an entity's own root -
// a Quantum entity view rewrites its root transform every frame, which would fight this.
// Phase is randomized per instance so several props side by side don't bob in lockstep.
public class IdleFloatAnimation : MonoBehaviour
{
    [SerializeField, Tooltip("Peak offset above/below the rest position, in local units.")]
    private float amplitude = 0.1f;

    [SerializeField, Tooltip("Full up-and-down cycles per second.")]
    private float frequency = 0.5f;

    [SerializeField, Tooltip("Start each instance at a random point of the wave so a group doesn't move in sync.")]
    private bool randomizePhase = true;

    [SerializeField, Tooltip("Keep bobbing while the game is paused (Time.timeScale = 0) - e.g. on a pause-time reveal card.")]
    private bool useUnscaledTime;

    private Vector3 _restLocalPosition;
    private float _phase;

    private void OnEnable()
    {
        _restLocalPosition = transform.localPosition;
        _phase = randomizePhase ? Random.value * Mathf.PI * 2f : 0f;
    }

    private void OnDisable()
    {
        transform.localPosition = _restLocalPosition;
    }

    private void Update()
    {
        float time = useUnscaledTime ? Time.unscaledTime : Time.time;
        float offset = Mathf.Sin(time * frequency * Mathf.PI * 2f + _phase) * amplitude;

        transform.localPosition = _restLocalPosition + Vector3.up * offset;
    }
}
