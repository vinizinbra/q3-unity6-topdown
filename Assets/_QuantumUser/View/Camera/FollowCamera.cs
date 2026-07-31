using System.Collections.Generic;
using UnityEngine;

public class FollowCamera : PgSingleton<FollowCamera>
{
    public Vector3 offset;
    public float speed;

    [Header("Multi-target framing")]
    [Tooltip("Zoom multiplier applied to offset when all targets sit on top of each other.")]
    public float minZoom = 1f;
    [Tooltip("Zoom multiplier applied to offset once targets are spreadReference (or more) apart.")]
    public float maxZoom = 2.2f;
    [Tooltip("World-unit distance from the framed center that maps to maxZoom.")]
    public float spreadReference = 10f;
    [Tooltip("How fast the zoom itself eases toward its desired value, independent of position speed.")]
    public float zoomLerpSpeed = 5f;

    private readonly List<Transform> _targets = new List<Transform>();
    private float _zoom = 1f;

    public void AddTarget(Transform target)
    {
        if (target != null && _targets.Contains(target) == false)
            _targets.Add(target);
    }

    public void RemoveTarget(Transform target)
    {
        _targets.Remove(target);
    }

    private void Update()
    {
        _targets.RemoveAll(t => t == null);
        if (_targets.Count == 0)
            return;

        Vector3 center = Vector3.zero;
        foreach (var t in _targets)
            center += t.position;
        center /= _targets.Count;

        float spread = 0f;
        foreach (var t in _targets)
            spread = Mathf.Max(spread, Vector3.Distance(t.position, center));

        float desiredZoom = Mathf.Clamp(1f + spread / spreadReference, minZoom, maxZoom);
        _zoom = Mathf.Lerp(_zoom, desiredZoom, Time.deltaTime * zoomLerpSpeed);

        Vector3 desiredPosition = center + offset * _zoom;
        transform.position = Vector3.Lerp(transform.position, desiredPosition, Time.deltaTime * speed);
    }
}
