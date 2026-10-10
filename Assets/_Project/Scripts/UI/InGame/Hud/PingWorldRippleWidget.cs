using System.Collections.Generic;
using UnityEngine;

// 3D half of the ping (see docs/ping.md): pulsing ground ripples where a player pinged, tinted with
// their colour, so the team can find the spot in the world and not only on the minimap. One per
// player - a new ping replaces their previous one. Built from code (flat SpriteRenderers on the
// ground plane), no prefab.
public class PingWorldRippleWidget : MonoBehaviour
{
    private const float Lifetime = 8f;
    private const float FadeTime = 1.5f;
    private const float PulsePeriod = 1.2f;
    private const float MaxRadius = 2.2f;      // world units
    private const float LiftAboveGround = 0.02f;   // just above the surface to avoid z-fighting
    private const float GroundSnapRayHeight = 20f;
    private const int SortingOrder = -2;

    private static readonly Dictionary<int, PingWorldRippleWidget> Active = new Dictionary<int, PingWorldRippleWidget>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Active.Clear();

    private SpriteRenderer[] _rings;
    private SpriteRenderer _dot;
    private Color _color;
    private float _startTime;
    private int _key;

    public static void Spawn(Quantum.PlayerRef player, Vector3 position, Color color)
    {
        int key = (int)player;
        if (Active.TryGetValue(key, out PingWorldRippleWidget old) && old != null)
            Destroy(old.gameObject);

        var go = new GameObject("PingWorldRipple");
        go.transform.position = SnapToGround(position) + Vector3.up * LiftAboveGround;
        var ripple = go.AddComponent<PingWorldRippleWidget>();
        ripple._key = key;
        ripple._color = color;
        ripple._startTime = Time.unscaledTime;
        ripple.Build();
        Active[key] = ripple;
    }

    // View-layer placement, same idiom as GroundWarningTelegraphManager.SnapToGround: the ping
    // position is the pinger's/target's own Y (feet or body height), not the floor under it.
    private static Vector3 SnapToGround(Vector3 position)
    {
        int mask = LayerMask.GetMask("Ground");
        if (mask != 0 && Physics.Raycast(position + Vector3.up * GroundSnapRayHeight, Vector3.down, out RaycastHit hit, GroundSnapRayHeight * 2f, mask))
            position.y = hit.point.y;

        return position;
    }

    private void Build()
    {
        Sprite ring = PingPresentation.GetSprite(ring: true);
        Sprite dot = PingPresentation.GetSprite(ring: false);

        _rings = new SpriteRenderer[2];
        for (int i = 0; i < _rings.Length; i++)
            _rings[i] = CreateRenderer("Ring" + i, ring, 0.3f);

        _dot = CreateRenderer("Dot", dot, 0.35f);
    }

    // Lies flat on the ground (sprites face +Z by default, so tip onto the XZ plane).
    private SpriteRenderer CreateRenderer(string name, Sprite sprite, float diameter)
    {
        var child = new GameObject(name);
        child.transform.SetParent(transform, false);
        child.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        var renderer = child.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = SortingOrder;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return renderer;
    }

    private void Update()
    {
        float age = Time.unscaledTime - _startTime;
        if (age >= Lifetime)
        {
            Destroy(gameObject);
            return;
        }

        float fade = Mathf.Clamp01((Lifetime - age) / FadeTime);

        // Two rings half a period apart so there's always one expanding.
        for (int i = 0; i < _rings.Length; i++)
        {
            float t = ((age / PulsePeriod) + i * 0.5f) % 1f;
            float radius = Mathf.Lerp(0.25f, MaxRadius, 1f - (1f - t) * (1f - t));
            _rings[i].transform.localScale = Vector3.one * (radius * 2f / _rings[i].sprite.bounds.size.x);

            Color c = _color;
            c.a = (1f - t) * fade;
            _rings[i].color = c;
        }

        float dotSize = 0.35f + 0.05f * Mathf.Sin(age * 6f);
        _dot.transform.localScale = Vector3.one * (dotSize / _dot.sprite.bounds.size.x);
        Color dc = _color;
        dc.a = fade;
        _dot.color = dc;
    }

    private void OnDestroy()
    {
        if (Active.TryGetValue(_key, out PingWorldRippleWidget current) && current == this)
            Active.Remove(_key);
    }
}
