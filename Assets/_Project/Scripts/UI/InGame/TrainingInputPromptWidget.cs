using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Device-aware button prompt on a Training Zone ground label (added by TrainingZoneBuilder next to the
// TextMeshPro) - see docs/training-zone.md. Draws keyboard keycaps or Xbox buttons in front of the label's
// own text (the suffix) and swaps them live when InputDeviceMode flips. Glyphs are built from code (flat
// SpriteRenderers + TextMeshPro letters, no art): one shared look for both devices - dark outline with a
// thicker bottom lip around a flat face. Keycaps are white with a dark letter, pad buttons slate with the
// Xbox letter colour. Children of the label, so TrainingZoneGuideWidget's pop scales them with it; built in
// Start, after the guide has collected (and hidden) the station labels in Awake. Everything animates: buttons and
// keys press (face sinks onto the lip) on a loop. The stick is a cap over a stem: the cap tilts up / left / down /
// right in turn (a glowing arrow lights the pushed side), then sweeps a full circle, and the W A S D cluster presses
// the key matching the stick's direction on the same clock.
public class TrainingInputPromptWidget : MonoBehaviour
{
    public enum GamepadGlyph
    {
        A,
        B,
        X,
        Y,
        LB,
        RB,
        LeftStick,
        RightStick,
    }

    private static readonly Color OutlineColor = new Color32(0x1E, 0x1C, 0x28, 0xFF);
    private static readonly Color KeyFaceColor = Color.white;
    private static readonly Color PadFaceColor = new Color32(0x40, 0x3D, 0x52, 0xFF);
    private static readonly Color PadLetterColor = Color.white;
    private static readonly Color ColorA = new Color32(0x45, 0xEB, 0x4A, 0xFF);
    private static readonly Color ColorB = new Color32(0xFF, 0x55, 0x55, 0xFF);
    private static readonly Color ColorX = new Color32(0x2E, 0xA8, 0xE3, 0xFF);
    private static readonly Color ColorY = new Color32(0xFF, 0xD5, 0x15, 0xFF);

    [Tooltip("One keycap per entry, e.g. Shift or E.")]
    [SerializeField] private string[] keyboardKeys = { "E" };
    [Tooltip("Lay the keycaps out as an inverted T (first key on top) - for W A S D.")]
    [SerializeField] private bool keyCluster;
    [SerializeField] private GamepadGlyph gamepadButton = GamepadGlyph.X;
    [Tooltip("Text after the glyphs (the label's own text is replaced by this). Empty = glyphs only.")]
    [SerializeField] private string suffix = "";

    [Tooltip("Glyph height as a multiple of the label's line height.")]
    [SerializeField] private float glyphScale = 1.25f;
    [Tooltip("Glyph letter size as a multiple of the label's font size.")]
    [SerializeField] private float letterScale = 0.8f;
    [Tooltip("Outline thickness / bottom lip as a fraction of the glyph height.")]
    [SerializeField] private float outline = 0.08f;
    [SerializeField] private float lip = 0.16f;
    [Tooltip("Space between glyphs, and between the glyphs and the suffix, as a fraction of the glyph height.")]
    [SerializeField] private float spacing = 0.3f;
    [Tooltip("Negative so heroes and enemies standing on the prompt draw over it (same as the ping ripple).")]
    [SerializeField] private int sortingOrder = -8;
    [Tooltip("Seconds for one press loop of a button / key.")]
    [SerializeField] private float pressPeriod = 1.4f;
    [Tooltip("Stick cap diameter as a multiple of the glyph height.")]
    [SerializeField] private float stickSize = 1.5f;
    [Tooltip("Seconds per direction tap (up, left, down, right - also W A S D).")]
    [SerializeField] private float stickStepDuration = 0.6f;
    [Tooltip("Seconds of the full-circle sweep after the four taps.")]
    [SerializeField] private float stickSweepDuration = 1.4f;
    [Tooltip("How far the stick cap tilts, as a fraction of its diameter.")]
    [SerializeField] private float stickTravel = 0.16f;

    private static readonly Color StickRimColor = new Color32(0x6E, 0x6A, 0x85, 0xFF);
    private static readonly Color GlowColor = Color.white;
    // Up, left, down, right - the tap order, the counter-clockwise sweep order, and the W A S D key order.
    private static readonly Vector2[] MoveDirections = { Vector2.up, Vector2.left, Vector2.down, Vector2.right };

    private static Sprite _circle;
    private static Sprite _roundedRect;
    private static Sprite _triangle;
    private static Sprite _glow;

    private TextMeshPro _label;
    private Transform _keyboardGroup;
    private Transform _gamepadGroup;
    private float _keyboardWidth;
    private float _gamepadWidth;
    private float _height;
    private Transform _stick;
    private float _stickDiameter;
    private readonly SpriteRenderer[] _arrows = new SpriteRenderer[4];
    private readonly SpriteRenderer[] _glows = new SpriteRenderer[4];
    // Every pressable face (face + letter). For a W A S D cluster these are in key order: W, A, S, D, which is
    // also MoveDirections' order.
    private readonly List<Transform> _tops = new List<Transform>();

    private void Start()
    {
        _label = GetComponent<TextMeshPro>();
        if (_label == null)
            return;

        _height = _label.GetPreferredValues("W").y * glyphScale;
        _keyboardGroup = BuildKeyboard(out _keyboardWidth);
        _gamepadGroup = BuildGamepad(out _gamepadWidth);
        Apply();

        InputDeviceMode.Changed += Apply;
    }

    private void OnDestroy()
    {
        InputDeviceMode.Changed -= Apply;
    }

    // Unscaled - keeps moving through a tutorial/level-up pause, like the guide's pop.
    private void Update()
    {
        if (_label == null)
            return;

        float time = Time.unscaledTime;
        float depth = _height * lip * 0.7f;

        if (_stick != null || keyCluster)
        {
            StickState(time, out Vector2 direction, out float amount, out int active);

            if (_stick != null)
            {
                _stick.localPosition = direction * (amount * stickTravel * _stickDiameter);

                for (int i = 0; i < _arrows.Length; i++)
                {
                    float lit = i == active ? amount : 0f;
                    _arrows[i].color = new Color(GlowColor.r, GlowColor.g, GlowColor.b, lit);
                    _glows[i].color = new Color(GlowColor.r, GlowColor.g, GlowColor.b, lit * 0.55f);
                }
            }

            if (keyCluster && _tops.Count == 4)
            {
                for (int i = 0; i < _tops.Count; i++)
                    _tops[i].localPosition = new Vector3(0f, i == active ? -amount * depth : 0f, 0f);

                return;
            }
        }

        float press = Press(time / pressPeriod % 1f) * depth;

        foreach (Transform top in _tops)
            top.localPosition = new Vector3(0f, -press, 0f);
    }

    // The move loop: four taps (push to the edge, hold, back) up / left / down / right, then one full-tilt sweep
    // counter-clockwise from up. `active` is the cardinal direction nearest the push (MoveDirections index).
    private void StickState(float time, out Vector2 direction, out float amount, out int active)
    {
        float taps = stickStepDuration * 4f;
        float t = time % (taps + stickSweepDuration);

        if (t < taps)
        {
            active = Mathf.Min(Mathf.FloorToInt(t / stickStepDuration), 3);
            direction = MoveDirections[active];
            amount = Press(t / stickStepDuration % 1f);
            return;
        }

        float sweep = (t - taps) / stickSweepDuration;
        float angle = 90f + sweep * 360f;
        direction = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
        amount = Mathf.SmoothStep(0f, 1f, Mathf.Min(sweep, 1f - sweep) / 0.12f);
        active = Mathf.RoundToInt(sweep * 4f) % 4;
    }

    // 0..1 press depth over one loop: quick push, hold, release, rest.
    private static float Press(float phase)
    {
        if (phase < 0.15f)
            return Mathf.SmoothStep(0f, 1f, phase / 0.15f);
        if (phase < 0.45f)
            return 1f;
        if (phase < 0.6f)
            return Mathf.SmoothStep(1f, 0f, (phase - 0.45f) / 0.15f);
        return 0f;
    }

    // Centres glyphs + suffix as one line on the label's anchor: the glyph group sits at the left end, and the
    // (centre-aligned) text is pushed right by a one-sided margin.
    private void Apply()
    {
        bool gamepad = InputDeviceMode.UsingGamepad;
        _keyboardGroup.gameObject.SetActive(gamepad == false);
        _gamepadGroup.gameObject.SetActive(gamepad);

        float glyphWidth = gamepad ? _gamepadWidth : _keyboardWidth;
        bool hasText = string.IsNullOrEmpty(suffix) == false;
        float textWidth = hasText ? _label.GetPreferredValues(suffix).x : 0f;
        float gap = hasText ? _height * spacing : 0f;
        float total = glyphWidth + gap + textWidth;

        Transform group = gamepad ? _gamepadGroup : _keyboardGroup;
        group.localPosition = new Vector3(-total * 0.5f + glyphWidth * 0.5f, 0f, 0f);

        float textCentre = -total * 0.5f + glyphWidth + gap + textWidth * 0.5f;
        _label.text = suffix;
        _label.margin = textCentre >= 0f ? new Vector4(textCentre * 2f, 0f, 0f, 0f) : new Vector4(0f, 0f, -textCentre * 2f, 0f);
    }

    // ---------------------------------------------------------------- keyboard

    private Transform BuildKeyboard(out float width)
    {
        Transform group = CreateGroup("Keyboard");
        float gap = _height * spacing * 0.5f;

        if (keyCluster && keyboardKeys.Length > 1)
        {
            // Inverted T: two rows of smaller keys, the first key centred over the rest.
            float key = _height * 0.8f;
            float rowGap = gap * 0.6f;
            float rowWidth = (keyboardKeys.Length - 1) * (key + gap) - gap;
            float topY = (key + rowGap) * 0.5f;

            Keycap(group, keyboardKeys[0], new Vector2(0f, topY), key);

            for (int i = 1; i < keyboardKeys.Length; i++)
                Keycap(group, keyboardKeys[i], new Vector2(-rowWidth * 0.5f + key * 0.5f + (i - 1) * (key + gap), -topY), key);

            width = rowWidth;
            return group;
        }

        var widths = new List<float>();
        float total = -gap;

        foreach (string key in keyboardKeys)
        {
            float w = Mathf.Max(_height, LetterWidth(key, _height) + _height * 0.5f);
            widths.Add(w);
            total += w + gap;
        }

        float x = -total * 0.5f;

        for (int i = 0; i < keyboardKeys.Length; i++)
        {
            Keycap(group, keyboardKeys[i], new Vector2(x + widths[i] * 0.5f, 0f), _height, widths[i]);
            x += widths[i] + gap;
        }

        width = Mathf.Max(0f, total);
        return group;
    }

    private void Keycap(Transform parent, string key, Vector2 centre, float height, float width = 0f)
    {
        if (width <= 0f)
            width = height;

        Transform cap = CreateGroup("Key_" + key, parent);
        cap.localPosition = centre;
        float radius = height * 0.24f;
        float t = height * outline;
        float l = height * lip;

        // Outline + lip as one dark block (taller by the lip, extending down), face inset on top of it.
        RoundedRect(cap, "Outline", new Vector2(0f, -l * 0.5f), new Vector2(width, height + l), radius, OutlineColor, 0);
        Transform top = Top(cap);
        RoundedRect(top, "Face", Vector2.zero, new Vector2(width - t * 2f, height - t * 2f), radius - t, KeyFaceColor, 1);
        Letter(top, key, height, OutlineColor);
    }

    // ---------------------------------------------------------------- gamepad

    private Transform BuildGamepad(out float width)
    {
        Transform group = CreateGroup("Gamepad");
        float h = _height;
        float t = h * outline;
        float l = h * lip;

        switch (gamepadButton)
        {
            case GamepadGlyph.LB:
            case GamepadGlyph.RB:
                width = h * 1.7f;
                RoundedRect(group, "Outline", new Vector2(0f, -l * 0.5f), new Vector2(width, h + l), h * 0.3f, OutlineColor, 0);
                Transform bumper = Top(group);
                RoundedRect(bumper, "Face", Vector2.zero, new Vector2(width - t * 2f, h - t * 2f), h * 0.3f - t, PadFaceColor, 1);
                Letter(bumper, gamepadButton.ToString(), h, PadLetterColor);
                return group;

            case GamepadGlyph.LeftStick:
            case GamepadGlyph.RightStick:
                BuildStick(group, out width);
                return group;

            default:
                width = h;
                Circle(group, "Lip", new Vector2(0f, -l), h, OutlineColor, 0);
                Circle(group, "Outline", Vector2.zero, h, OutlineColor, 0);
                Transform button = Top(group);
                Circle(button, "Face", Vector2.zero, h - t * 2f, PadFaceColor, 1);
                Letter(button, gamepadButton.ToString(), h, FaceButtonColor(gamepadButton));
                return group;
        }
    }

    // Thumbstick seen from above at an angle: a dark stem showing under a big cap (outline, light rim, dark face,
    // letter). Update tilts the cap; the stem stays, which reads as the tilt. Glowing arrows sit just outside the
    // cap, one per direction, lit while the cap pushes that way.
    private void BuildStick(Transform group, out float width)
    {
        float d = _height * stickSize;
        float t = _height * outline;
        _stickDiameter = d;
        width = d;

        Circle(group, "Stem", new Vector2(0f, -d * 0.3f), d * 0.62f, OutlineColor, 0);

        float arrowSize = d * 0.24f;
        float arrowRadius = d * 0.5f + d * stickTravel + arrowSize * 0.55f;

        for (int i = 0; i < MoveDirections.Length; i++)
        {
            Vector2 at = MoveDirections[i] * arrowRadius;
            float rotation = Vector2.SignedAngle(Vector2.up, MoveDirections[i]);

            _glows[i] = Shape(group, "Glow" + i, GlowSprite, at, 0);
            _glows[i].transform.localScale = Vector3.one * arrowSize * 2.6f;

            _arrows[i] = Shape(group, "Arrow" + i, TriangleSprite, at, 1);
            _arrows[i].transform.localScale = Vector3.one * arrowSize;
            _arrows[i].transform.localRotation = Quaternion.Euler(0f, 0f, rotation);
        }

        _stick = CreateGroup("Stick", group);
        Circle(_stick, "Outline", Vector2.zero, d, OutlineColor, 2);
        Circle(_stick, "Rim", Vector2.zero, d - t * 2f, StickRimColor, 3);
        Circle(_stick, "Face", Vector2.zero, d * 0.7f, PadFaceColor, 4);
        Letter(_stick, gamepadButton == GamepadGlyph.LeftStick ? "L" : "R", _height, PadLetterColor, 5, stickSize);
    }

    private static Color FaceButtonColor(GamepadGlyph glyph)
    {
        switch (glyph)
        {
            case GamepadGlyph.A: return ColorA;
            case GamepadGlyph.B: return ColorB;
            case GamepadGlyph.Y: return ColorY;
            default: return ColorX;
        }
    }

    // ---------------------------------------------------------------- building blocks

    private Transform CreateGroup(string name, Transform parent = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent != null ? parent : transform, false);
        return go.transform;
    }

    // A pressable face group - Update sinks it onto the lip.
    private Transform Top(Transform parent)
    {
        Transform top = CreateGroup("Top", parent);
        _tops.Add(top);
        return top;
    }

    // Children stay in the label's own plane (the label is already laid flat on the ground), nudged up off it.
    private SpriteRenderer Shape(Transform parent, string name, Sprite sprite, Vector2 centre, int layer)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(centre.x, centre.y, -0.01f);

        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = sortingOrder + layer;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return renderer;
    }

    private void Circle(Transform parent, string name, Vector2 centre, float diameter, Color color, int layer)
    {
        SpriteRenderer renderer = Shape(parent, name, CircleSprite, centre, layer);
        renderer.color = color;
        renderer.transform.localScale = Vector3.one * diameter;
    }

    // Sliced sprite whose corners are 1 unit, scaled so the corners come out at `radius`.
    private void RoundedRect(Transform parent, string name, Vector2 centre, Vector2 size, float radius, Color color, int layer)
    {
        radius = Mathf.Max(0.001f, radius);
        SpriteRenderer renderer = Shape(parent, name, RoundedRectSprite, centre, layer);
        renderer.color = color;
        renderer.drawMode = SpriteDrawMode.Sliced;
        renderer.size = size / radius;
        renderer.transform.localScale = Vector3.one * radius;
    }

    private void Letter(Transform parent, string text, float height, Color color, int layer = 2, float sizeScale = 1f)
    {
        var go = new GameObject("Letter");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, 0f, -0.02f);

        var tmp = go.AddComponent<TextMeshPro>();
        tmp.font = _label.font;
        tmp.text = text;
        tmp.fontSize = _label.fontSize * letterScale * sizeScale * (text.Length > 1 ? 0.8f : 1f);
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.rectTransform.sizeDelta = new Vector2(height * 3f, height);
        tmp.sortingOrder = sortingOrder + layer;

        MeshRenderer renderer = go.GetComponent<MeshRenderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    private float LetterWidth(string text, float height)
    {
        float fontSize = _label.fontSize * letterScale * (text.Length > 1 ? 0.8f : 1f);
        float saved = _label.fontSize;
        _label.fontSize = fontSize;
        float width = _label.GetPreferredValues(text).x;
        _label.fontSize = saved;
        return width;
    }

    // ---------------------------------------------------------------- sprites

    // 1-unit circle.
    private static Sprite CircleSprite
    {
        get
        {
            if (_circle == null)
                _circle = CreateShapeSprite(128, 64f, 128f, Vector4.zero);
            return _circle;
        }
    }

    // 9-sliced rounded square, corners 1 unit.
    private static Sprite RoundedRectSprite
    {
        get
        {
            if (_roundedRect == null)
                _roundedRect = CreateShapeSprite(64, 24f, 24f, new Vector4(24f, 24f, 24f, 24f));
            return _roundedRect;
        }
    }

    // 1-unit triangle pointing up (+y).
    private static Sprite TriangleSprite
    {
        get
        {
            if (_triangle == null)
                _triangle = CreateSprite(64, 64f, (x, y) =>
                {
                    // Distance inside each of the three edges of (0.1,0.12) (0.9,0.12) (0.5,0.9); min = inside the triangle.
                    float bottom = y - 0.12f;
                    float left = Vector2.Dot(new Vector2(x - 0.1f, y - 0.12f), new Vector2(0.78f, -0.4f).normalized);
                    float right = Vector2.Dot(new Vector2(x - 0.9f, y - 0.12f), new Vector2(-0.78f, -0.4f).normalized);
                    return Mathf.Min(bottom, Mathf.Min(left, right)) * 64f + 0.5f;
                });
            return _triangle;
        }
    }

    // 1-unit soft radial glow.
    private static Sprite GlowSprite
    {
        get
        {
            if (_glow == null)
                _glow = CreateSprite(64, 64f, (x, y) =>
                {
                    float r = Mathf.Clamp01(Vector2.Distance(new Vector2(x, y), new Vector2(0.5f, 0.5f)) * 2f);
                    return (1f - r) * (1f - r);
                });
            return _glow;
        }
    }

    // White sprite whose alpha is `alpha(u, v)` (0..1 texture coords, clamped).
    private static Sprite CreateSprite(int size, float pixelsPerUnit, System.Func<float, float, float> alpha)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "TrainingPromptShape",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };

        var pixels = new Color32[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha((x + 0.5f) / size, (y + 0.5f) / size)) * 255f));
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), pixelsPerUnit);
    }

    // White anti-aliased rounded square (a circle when radius = half the size), tinted per renderer.
    private static Sprite CreateShapeSprite(int size, float radius, float pixelsPerUnit, Vector4 border)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "TrainingPromptShape",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };

        var pixels = new Color32[size * size];
        float half = size * 0.5f;
        float inner = half - radius;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float qx = Mathf.Max(Mathf.Abs(x + 0.5f - half) - inner, 0f);
                float qy = Mathf.Max(Mathf.Abs(y + 0.5f - half) - inner, 0f);
                float distance = Mathf.Sqrt(qx * qx + qy * qy) - radius;
                byte alpha = (byte)(Mathf.Clamp01(0.5f - distance) * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), pixelsPerUnit, 0, SpriteMeshType.FullRect, border);
    }
}
