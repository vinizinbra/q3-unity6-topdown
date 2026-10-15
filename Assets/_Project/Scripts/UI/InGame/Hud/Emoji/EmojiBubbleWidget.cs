using System.Collections.Generic;
using Quantum;
using UnityEngine;

// World-space emoji above a hero's head (see docs/emoji.md): pops in with an overshoot, bobs, then pops
// out. A camera-facing SpriteRenderer (so the sheet packs in the Gameplay atlas), built from code - no
// prefab. One per player: a new emoji replaces the previous one instantly. Size/height below are the
// shared defaults; each hero can adjust them with CharacterData.EmojiOffset/EmojiScale (read live, so they
// can be tuned in Play mode).
public class EmojiBubbleWidget : MonoBehaviour
{
    private const float Lifetime = 2.5f;
    private const float PopInTime = 0.25f;
    private const float PopOutTime = 0.2f;
    private const float WorldSize = 1.3f;          // world units across
    private const float HeadClearance = 0.35f;     // above the top of the hero's renderers
    private const float MinHeight = 1.2f;
    private const float MaxHeight = 4f;
    private const float BobAmplitude = 0.06f;
    private const float BobSpeed = 5f;
    private const int SortingOrder = 500;

    private static readonly Dictionary<int, EmojiBubbleWidget> Active = new Dictionary<int, EmojiBubbleWidget>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Active.Clear();

    private SpriteRenderer _renderer;
    private Transform _target;
    private CharacterData _data;
    private float _height;
    private float _baseScale;
    private float _startTime;
    private int _key;

    public static void Spawn(PlayerRef player, Transform target, Sprite sprite, CharacterData data)
    {
        if (target == null || sprite == null)
            return;

        int key = (int)player;
        if (Active.TryGetValue(key, out EmojiBubbleWidget old) && old != null)
            Destroy(old.gameObject);

        var go = new GameObject("EmojiBubble");
        var bubble = go.AddComponent<EmojiBubbleWidget>();
        bubble._key = key;
        bubble._target = target;
        bubble._data = data;
        bubble._height = ResolveHeight(target);
        bubble._startTime = Time.unscaledTime;
        bubble.Build(sprite);
        Active[key] = bubble;
        bubble.LateUpdate();
    }

    // Top of the hero's own sprites, so a tall hero and a short one both get the bubble just above the head.
    private static float ResolveHeight(Transform target)
    {
        float top = float.MinValue;
        foreach (SpriteRenderer r in target.GetComponentsInChildren<SpriteRenderer>())
        {
            if (r.enabled && r.sprite != null)
                top = Mathf.Max(top, r.bounds.max.y);
        }

        float height = top == float.MinValue ? MinHeight : top - target.position.y + HeadClearance;
        return Mathf.Clamp(height, MinHeight, MaxHeight);
    }

    private void Build(Sprite sprite)
    {
        _renderer = gameObject.AddComponent<SpriteRenderer>();
        _renderer.sprite = sprite;
        _renderer.sortingOrder = SortingOrder;
        _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _renderer.receiveShadows = false;
        _baseScale = WorldSize / Mathf.Max(0.01f, sprite.bounds.size.x);
    }

    private void LateUpdate()
    {
        float age = Time.unscaledTime - _startTime;
        if (_target == null || age >= Lifetime)
        {
            Destroy(gameObject);
            return;
        }

        float scale;
        if (age < PopInTime)
            scale = EaseOutBack(age / PopInTime);
        else if (age > Lifetime - PopOutTime)
            scale = 1f - Mathf.SmoothStep(0f, 1f, (age - (Lifetime - PopOutTime)) / PopOutTime);
        else
            scale = 1f;

        Vector3 offset = _data != null ? _data.EmojiOffset : Vector3.zero;
        float heroScale = _data != null && _data.EmojiScale > 0f ? _data.EmojiScale : 1f;

        float bob = Mathf.Sin(age * BobSpeed) * BobAmplitude;
        transform.position = _target.position + Vector3.up * (_height + bob) + offset;
        transform.localScale = Vector3.one * (_baseScale * heroScale * scale);

        Camera cam = Camera.main;
        if (cam != null)
            transform.rotation = cam.transform.rotation;
    }

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float u = t - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }

    private void OnDestroy()
    {
        if (Active.TryGetValue(_key, out EmojiBubbleWidget current) && current == this)
            Active.Remove(_key);
    }
}
