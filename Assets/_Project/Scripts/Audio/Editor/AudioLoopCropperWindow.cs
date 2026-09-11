using QuantumUser.View.Util;
using UnityEditor;
using UnityEngine;

namespace Project.Audio.EditorTools
{
    // Author + test + bake a "loop with an intro" for one clip inside a SoundData:
    //
    //   Start At    - where playback begins (skips dead air on the head).
    //   Loop Start  - where it jumps back to once it reaches End At. Equal to Start At = a plain
    //                 full-region loop; later than Start At = an intro that plays once.
    //   End At      - where the loop wraps.
    //
    // "Detect BPM" gives a starting tempo grid (see AudioBpmEstimator) so the three markers can be
    // snapped onto a beat/bar instead of landing mid-transient, which is what actually causes an
    // audible seam. "Play Loop" writes the current markers onto the clip's own SoundClip override and
    // auditions it through the real AudioManager (SoundDataEditorPreview), so what's heard here is
    // exactly what the game will play - including the loop jump itself, in Edit Mode, no Play Mode
    // needed. "Crop & Save New File" is the separate, deliberate step that shrinks the shipped asset:
    // it writes a new file containing only [Start At, End At] (see AudioLoopCropper) and re-points
    // this clip at it - the old source is left alone.
    internal class AudioLoopCropperWindow : EditorWindow
    {
        private const string LogTag = "AudioLoop";
        private const int WaveHeight = 160;
        private const int TickHeight = 6;
        private const int BarTickHeight = 20;

        private SoundData _data;
        private int _variantIndex;
        private AudioClip _clip;
        private AudioSilenceSplitter.ClipData _clipData;

        private float _start;
        private float _loopStart;
        private float _end;

        private float _bpm;
        private float _beatOffset;
        private int _beatsPerStep = 4;

        private Texture2D _preview;
        private int _previewWidth;
        private int _dragMarker = -1; // -1 none, 0 = start, 1 = loopStart, 2 = end.

        private SoundHandle _previewHandle;

        [MenuItem("Tools/RiftRaiders/Audio/Loop Point Editor")]
        private static void OpenEmpty() => Open(null);

        internal static void Open(SoundData data)
        {
            var window = GetWindow<AudioLoopCropperWindow>("Loop Point Editor");
            window.minSize = new Vector2(420f, 420f);
            window.SetTarget(data);
        }

        private void SetTarget(SoundData data)
        {
            _data = data;
            _variantIndex = 0;
            LoadVariant();
        }

        private void OnEnable() => EditorApplication.update += OnEditorUpdate;

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;

            if (_data != null)
                SoundDataEditorPreview.Stop(_data);
        }

        // Repaints while (and only while) something is actually playing, so the playhead moves live -
        // an EditorWindow otherwise only redraws on input or when something else asks it to.
        private void OnEditorUpdate()
        {
            if (_previewHandle.IsPlaying)
                Repaint();
        }

        private void OnGUI()
        {
            EditorGUI.BeginChangeCheck();
            var data = (SoundData)EditorGUILayout.ObjectField("Sound Data", _data, typeof(SoundData), false);
            if (EditorGUI.EndChangeCheck())
                SetTarget(data);

            if (_data == null)
            {
                EditorGUILayout.HelpBox("Assign a SoundData, or open this from its Inspector (\"Loop Point Editor / Crop Audio...\" under Trim & Fade).", MessageType.Info);
                return;
            }

            if (_data.variants == null || _data.variants.Length == 0)
            {
                EditorGUILayout.HelpBox($"'{_data.name}' has no clips.", MessageType.Warning);
                return;
            }

            DrawVariantPicker();

            if (_clip == null)
            {
                EditorGUILayout.HelpBox("Selected variant has no clip assigned.", MessageType.Warning);
                return;
            }

            if (_clipData == null)
            {
                EditorGUILayout.HelpBox("Could not read samples from this clip - see the Console.", MessageType.Error);
                return;
            }

            DrawLoopToggle();

            EditorGUILayout.Space(4f);
            DrawWaveform();
            HandleWaveformInput();
            DrawMarkerFields();

            EditorGUILayout.Space(6f);
            DrawBpmControls();

            EditorGUILayout.Space(6f);
            DrawTransport();

            EditorGUILayout.Space(6f);
            DrawCropSection();
        }

        // ------------------------------------------------------------------ loading

        // `loop` is shared across the whole SoundData (see ApplyToVariant) - surfaced here, right at
        // the top, so it's never a silent reason "Preview Loop" plays once and fades instead of
        // looping.
        private void DrawLoopToggle()
        {
            EditorGUI.BeginChangeCheck();
            bool loop = EditorGUILayout.ToggleLeft("Loop (SoundData-wide)", _data.loop);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_data, "Toggle Loop");
                _data.loop = loop;
                EditorUtility.SetDirty(_data);
            }

            if (!_data.loop)
                EditorGUILayout.HelpBox("Loop is off - Preview Loop will play the trimmed region once and fade out instead of looping. It's turned back on automatically the next time you Preview, Mark, or Crop.", MessageType.Warning);
        }

        private void DrawVariantPicker()
        {
            if (_data.variants.Length < 2)
                return;

            string[] names = new string[_data.variants.Length];
            for (int i = 0; i < names.Length; i++)
            {
                AudioClip c = _data.variants[i]?.clip;
                names[i] = c != null ? c.name : $"(empty {i})";
            }

            EditorGUI.BeginChangeCheck();
            int index = EditorGUILayout.Popup("Clip", _variantIndex, names);
            if (EditorGUI.EndChangeCheck() && index != _variantIndex)
            {
                _variantIndex = index;
                LoadVariant();
            }
        }

        // Decodes the selected clip and seeds the markers from whatever's currently authored for it
        // (per-clip override if it has one ticked, otherwise the sound's shared trim) - the same
        // precedence AudioManager plays with, via SoundData.ResolveTrim/ResolveLoopStart.
        private void LoadVariant()
        {
            SoundDataEditorPreview.Stop(_data);
            _clip = null;
            _clipData = null;
            _preview = null;
            _previewWidth = 0;

            if (_data == null || _data.variants == null || _variantIndex >= _data.variants.Length)
                return;

            SoundClip variant = _data.variants[_variantIndex];
            _clip = variant?.clip;
            if (_clip == null)
                return;

            _clipData = AudioSilenceSplitter.Read(_clip);
            if (_clipData == null)
                return;

            _data.ResolveTrim(_clip, variant, out _start, out _end);
            _loopStart = _data.ResolveLoopStart(variant, _start, _end);
            _bpm = 0f;
            _beatOffset = 0f;
            DetectBpm(); // Primes the grid up front, so Mark Loop Start/End have something to snap to right away.
        }

        // ------------------------------------------------------------------ waveform

        private void DrawWaveform()
        {
            Rect rect = GUILayoutUtility.GetRect(10f, WaveHeight, GUILayout.ExpandWidth(true));
            int width = Mathf.Clamp(Mathf.RoundToInt(rect.width), 64, 2048);

            if (_preview == null || _previewWidth != width)
            {
                BuildPreview(width);
                _previewWidth = width;
            }

            GUI.DrawTexture(rect, _preview, ScaleMode.StretchToFill);
            _waveRect = rect;

            DrawBarLabels(rect);
        }

        private Rect _waveRect;

        // Bar ("compás") numbers overlaid on the waveform as plain GUI labels rather than baked into
        // the texture - text in a raw pixel buffer would need its own font atlas, a label on top of
        // the rect is free. Thinned out to every Nth bar once bars would land close enough to overlap,
        // so a fast song at a wide window doesn't turn into an unreadable wall of numbers.
        private void DrawBarLabels(Rect rect)
        {
            if (_bpm <= 0.01f || _clipData.Duration <= 0f)
                return;

            float duration = _clipData.Duration;
            float barPeriod = BarPeriod;
            float pixelsPerBar = barPeriod / duration * rect.width;
            int labelEvery = Mathf.Max(1, Mathf.CeilToInt(28f / Mathf.Max(1f, pixelsPerBar)));

            int bar = 0;
            for (float bt = _beatOffset; bt < duration; bt += barPeriod, bar++)
            {
                if (bar % labelEvery != 0)
                    continue;

                float x = rect.x + bt / duration * rect.width;
                GUI.Label(new Rect(x + 2f, rect.y + 1f, 32f, 14f), (bar + 1).ToString(), EditorStyles.whiteMiniLabel);
            }
        }

        private void BuildPreview(int width)
        {
            if (_preview == null || _preview.width != width)
            {
                if (_preview != null)
                    DestroyImmediate(_preview);

                _preview = new Texture2D(width, WaveHeight, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            }

            Color32 outside = new Color32(60, 45, 45, 255);
            Color32 intro = new Color32(120, 95, 50, 255);
            Color32 loopRegion = new Color32(60, 120, 80, 255);
            Color32 waveOutside = new Color32(100, 80, 80, 255);
            Color32 waveIntro = new Color32(210, 175, 90, 255);
            Color32 waveLoop = new Color32(120, 220, 150, 255);
            Color32 beatTick = new Color32(255, 255, 255, 40);
            Color32 barTick = new Color32(255, 255, 255, 150);

            float duration = _clipData.Duration;
            Color32[] pixels = new Color32[width * WaveHeight];
            int windows = _clipData.WindowDb.Length;

            for (int x = 0; x < width; x++)
            {
                float t = duration > 0f ? (float)x / width * duration : 0f;
                Color32 bg = t < _start || t >= _end ? outside : t < _loopStart ? intro : loopRegion;
                Color32 fg = t < _start || t >= _end ? waveOutside : t < _loopStart ? waveIntro : waveLoop;

                int from = Mathf.Clamp(Mathf.FloorToInt((float)x / width * windows), 0, Mathf.Max(0, windows - 1));
                int to = Mathf.Clamp(Mathf.FloorToInt((float)(x + 1) / width * windows), from + 1, windows);
                float peak = float.MinValue;
                for (int w = from; w < to; w++)
                    peak = Mathf.Max(peak, _clipData.WindowDb[w]);

                int height = Mathf.Clamp(Mathf.RoundToInt(Normalized(peak) * (WaveHeight - 1)), 0, WaveHeight - 1);

                for (int y = 0; y < WaveHeight; y++)
                    pixels[y * width + x] = y <= height ? fg : bg;
            }

            // Beat pulse (faint) + bar ("compás") lines (bold), drawn after the waveform so they're
            // never hidden under a loud column. The bars are the actual grid a loop point should
            // usually land on - musically, "loop from bar 5 to bar 21" is how this gets decided, not
            // a raw second count - so they're tall and bright enough to read as structure at a glance,
            // while individual beats stay a subtle reference underneath.
            if (_bpm > 0.01f && duration > 0f)
            {
                float beatPeriod = BeatPeriod;
                for (float bt = _beatOffset; bt < duration; bt += beatPeriod)
                {
                    int x = Mathf.Clamp(Mathf.RoundToInt(bt / duration * width), 0, width - 1);
                    for (int y = 0; y < TickHeight; y++)
                        pixels[y * width + x] = beatTick;
                }

                float barPeriod = BarPeriod;
                for (float bt = _beatOffset; bt < duration; bt += barPeriod)
                {
                    int x = Mathf.Clamp(Mathf.RoundToInt(bt / duration * width), 0, width - 1);
                    for (int y = 0; y < BarTickHeight; y++)
                        pixels[y * width + x] = barTick;
                }
            }

            Color32 startColor = new Color32(255, 255, 255, 255);
            Color32 loopColor = new Color32(255, 225, 60, 255);
            Color32 endColor = new Color32(230, 70, 70, 255);

            DrawMarkerLine(pixels, width, duration, _start, startColor);
            DrawMarkerLine(pixels, width, duration, _loopStart, loopColor);
            DrawMarkerLine(pixels, width, duration, _end, endColor);

            // Flag-shaped grips at the top of each line - the actual line is one pixel wide and easy
            // to miss with the mouse, so this is what the eye (and the cursor) actually aims for.
            DrawHandleFlag(pixels, width, duration, _start, startColor);
            DrawHandleFlag(pixels, width, duration, _loopStart, loopColor);
            DrawHandleFlag(pixels, width, duration, _end, endColor);

            // Playhead drawn last, on top of everything else - it's the thing your eye should find
            // first while the loop is auditioning. Its grip sits at the BOTTOM (the three loop
            // markers grip from the top), so the two never look interchangeable even when they land
            // on the same instant.
            if (_previewHandle.IsPlaying)
            {
                float t = _previewHandle.Time;
                if (t >= 0f)
                {
                    Color32 playheadColor = new Color32(80, 200, 255, 255);
                    DrawMarkerLine(pixels, width, duration, t, playheadColor);
                    DrawHandleFlag(pixels, width, duration, t, playheadColor, atTop: false);
                }
            }

            _preview.SetPixels32(pixels);
            _preview.Apply(false);
        }

        private static void DrawMarkerLine(Color32[] pixels, int width, float duration, float t, Color32 color)
        {
            if (duration <= 0f)
                return;

            int x = Mathf.Clamp(Mathf.RoundToInt(t / duration * width), 0, width - 1);
            for (int y = 0; y < WaveHeight; y++)
                pixels[y * width + x] = color;
        }

        // A small downward-pointing triangle at the top edge of the waveform (row 0 of the pixel
        // buffer is the BOTTOM of the drawn image, so "top" is the last rows) - a fatter, more
        // grabbable target than the 1px line, and it's what HandleGrabPixels below measures against.
        private const int HandleSize = 7;

        private static void DrawHandleFlag(Color32[] pixels, int width, float duration, float t, Color32 color, bool atTop = true)
        {
            if (duration <= 0f)
                return;

            int cx = Mathf.Clamp(Mathf.RoundToInt(t / duration * width), 0, width - 1);

            for (int row = 0; row < HandleSize; row++)
            {
                int y = atTop ? WaveHeight - 1 - row : row;
                if (y < 0 || y >= WaveHeight)
                    continue;

                int span = HandleSize - row;
                for (int dx = -span; dx <= span; dx++)
                {
                    int px = cx + dx;
                    if (px < 0 || px >= width)
                        continue;

                    pixels[y * width + px] = color;
                }
            }
        }

        private static float Normalized(float db) => Mathf.Clamp01((db + 80f) / 80f);

        // ------------------------------------------------------------------ marker interaction

        // A click has to actually land ON a handle to grab it - measured in screen pixels, not
        // seconds, so it works the same whether markers happen to sit a heartbeat or a minute apart.
        // Not just precision: without this, a click ANYWHERE in the waveform (to scrub, to just
        // click through) would silently yank the nearest marker to wherever the mouse landed.
        private const float HandleGrabPixels = 12f;

        private void HandleWaveformInput()
        {
            Event evt = Event.current;
            float duration = _clipData.Duration;
            if (duration <= 0f)
                return;

            if (evt.type == EventType.MouseDown && _waveRect.Contains(evt.mousePosition))
            {
                float mouseX = evt.mousePosition.x - _waveRect.x;
                int marker = NearestHandle(mouseX);
                if (marker < 0)
                    return;

                _dragMarker = marker;
                ApplyDrag(Mathf.Clamp01(mouseX / _waveRect.width) * duration);
                evt.Use();
            }
            else if (evt.type == EventType.MouseDrag && _dragMarker >= 0)
            {
                float t = Mathf.Clamp01((evt.mousePosition.x - _waveRect.x) / _waveRect.width) * duration;
                ApplyDrag(t);
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && _dragMarker >= 0)
            {
                // Persist to the asset once, at the END of the gesture - not every MouseDrag event,
                // which would spam an Undo entry per pixel of mouse movement. The live voice was
                // already kept in sync the whole time via PushLiveLoop in ApplyDrag.
                if (_dragMarker != 3)
                    ApplyToVariant();

                _dragMarker = -1;
                evt.Use();
            }
        }

        // -1 when the click didn't land within HandleGrabPixels of any handle. `mouseX` is already
        // relative to the waveform rect, same as the x this method converts each marker's time into.
        // 0/1/2 = Start/Loop Start/End; 3 = the live playhead (only a candidate while it's playing) -
        // grabbing it scrubs playback instead of moving a loop point.
        private int NearestHandle(float mouseX)
        {
            float duration = _clipData.Duration;
            float sx = _start / duration * _waveRect.width;
            float lx = _loopStart / duration * _waveRect.width;
            float ex = _end / duration * _waveRect.width;

            float best = Mathf.Abs(mouseX - sx);
            int marker = 0;

            float dLoop = Mathf.Abs(mouseX - lx);
            if (dLoop < best) { best = dLoop; marker = 1; }

            float dEnd = Mathf.Abs(mouseX - ex);
            if (dEnd < best) { best = dEnd; marker = 2; }

            if (_previewHandle.IsPlaying)
            {
                float playTime = _previewHandle.Time;
                if (playTime >= 0f)
                {
                    float px = playTime / duration * _waveRect.width;
                    float dPlay = Mathf.Abs(mouseX - px);
                    if (dPlay < best) { best = dPlay; marker = 3; }
                }
            }

            return best <= HandleGrabPixels ? marker : -1;
        }

        private void ApplyDrag(float t)
        {
            const float MinGap = 0.01f;
            float duration = _clipData.Duration;

            switch (_dragMarker)
            {
                case 0:
                    _start = Mathf.Clamp(t, 0f, _loopStart);
                    break;
                case 1:
                    _loopStart = Mathf.Clamp(t, _start, Mathf.Max(_start, _end - MinGap));
                    break;
                case 2:
                    _end = Mathf.Clamp(t, Mathf.Max(_start, _loopStart) + MinGap, duration);
                    break;
                case 3:
                    _previewHandle.SetTime(Mathf.Clamp(t, 0f, duration));
                    break;
            }

            PushLiveLoop();
            _previewWidth = 0; // Force the texture to redraw with the new marker positions.
            Repaint();
        }

        // Updates the ACTIVE voice's loop boundaries live, every frame of a drag, WITHOUT touching
        // the asset (no Undo/dirty) - that's what makes dragging while it's playing sound continuous
        // instead of only updating on release. See CommitLoopPoints for the persisted version.
        private void PushLiveLoop()
        {
            if (_previewHandle.IsPlaying)
                _previewHandle.UpdateLoop(_end, _loopStart);
        }

        // The "gesture finished" commit: writes the current markers to the asset (Undo + dirty, via
        // ApplyToVariant) AND pushes them to the live voice, for every mutation that ISN'T a drag
        // (drags already push live per-frame and persist once on mouse-up instead - see
        // HandleWaveformInput/ApplyDrag).
        private void CommitLoopPoints()
        {
            ApplyToVariant();
            PushLiveLoop();
            _previewWidth = 0;
            Repaint();
        }

        private void DrawMarkerFields()
        {
            float duration = _clipData.Duration;

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"Start {FormatTime(_start)}", GUILayout.Width(110f));
                EditorGUILayout.LabelField($"Loop {FormatTime(_loopStart)}", GUILayout.Width(110f));
                EditorGUILayout.LabelField($"End {FormatTime(_end)}", GUILayout.Width(110f));
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField($"Clip length {FormatTime(duration)}", EditorStyles.miniLabel);
            }

            EditorGUI.BeginChangeCheck();
            float start = EditorGUILayout.FloatField("Start At", _start);
            float loopStart = EditorGUILayout.FloatField("Loop Start", _loopStart);
            float end = EditorGUILayout.FloatField("End At", _end);
            if (EditorGUI.EndChangeCheck())
            {
                _start = Mathf.Clamp(start, 0f, duration);
                _end = Mathf.Clamp(end, _start + 0.01f, duration);
                _loopStart = Mathf.Clamp(loopStart, _start, _end - 0.01f);
                CommitLoopPoints();
            }

            float introLength = _loopStart - _start;
            float loopLength = _end - _loopStart;
            EditorGUILayout.LabelField(introLength > 0.01f
                ? $"Intro {introLength:0.00}s (plays once), then loops {loopLength:0.00}s forever."
                : $"Loops the whole {loopLength:0.00}s region forever.", EditorStyles.miniLabel);
        }

        private static string FormatTime(float seconds) => $"{(int)(seconds / 60f):00}:{seconds % 60f:00.00}";

        // ------------------------------------------------------------------ BPM

        private void DrawBpmControls()
        {
            EditorGUILayout.LabelField("Beat Grid & Bars (for finding the loop by ear/eye instead of by the second)", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Detect BPM", GUILayout.Width(90f)))
                    DetectBpm();

                _bpm = EditorGUILayout.FloatField(_bpm, GUILayout.Width(50f));
                EditorGUILayout.LabelField("BPM", GUILayout.Width(30f));

                _beatOffset = EditorGUILayout.FloatField(_beatOffset, GUILayout.Width(50f));
                EditorGUILayout.LabelField("offset (s)", GUILayout.Width(60f));

                _beatsPerStep = Mathf.Max(1, EditorGUILayout.IntField(_beatsPerStep, GUILayout.Width(30f)));
                EditorGUILayout.LabelField("beats/bar (time sig)", GUILayout.Width(110f));
            }

            using (new EditorGUI.DisabledScope(_bpm <= 0.01f))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Snap Start"))
                {
                    _start = Mathf.Clamp(SnapToGrid(_start), 0f, _loopStart);
                    CommitLoopPoints();
                }

                if (GUILayout.Button("Snap Loop"))
                {
                    _loopStart = Mathf.Clamp(SnapToGrid(_loopStart), _start, _end - 0.01f);
                    CommitLoopPoints();
                }

                if (GUILayout.Button("Snap End"))
                {
                    _end = Mathf.Clamp(SnapToGrid(_end), Mathf.Max(_start, _loopStart) + 0.01f, _clipData.Duration);
                    CommitLoopPoints();
                }

                if (GUILayout.Button("Snap All"))
                {
                    _start = SnapToGrid(_start);
                    _loopStart = Mathf.Max(SnapToGrid(_loopStart), _start);
                    _end = Mathf.Max(SnapToGrid(_end), _loopStart + 0.01f);
                    CommitLoopPoints();
                }
            }

            DrawBarFields();
        }

        private float BeatPeriod => _bpm > 0.01f ? 60f / _bpm : 0f;
        private float BarPeriod => BeatPeriod * Mathf.Max(1, _beatsPerStep);

        // 1-based, the way a musician counts them - "bar 1" is the first bar, not "bar 0".
        private int TimeToBar(float t) => BarPeriod > 0.0001f ? Mathf.RoundToInt((t - _beatOffset) / BarPeriod) + 1 : 1;
        private float BarToTime(int bar) => _beatOffset + (bar - 1) * BarPeriod;

        // Set a loop point by typing the bar it should land on instead of hunting for a second value
        // - "loop from bar 5 to bar 21" is how this gets decided musically, this just lets that be
        // typed directly.
        private void DrawBarFields()
        {
            if (_bpm <= 0.01f)
            {
                EditorGUILayout.HelpBox("Detect BPM (above) to set loop points by bar number instead of by the second.", MessageType.None);
                return;
            }

            float duration = _clipData.Duration;

            EditorGUI.BeginChangeCheck();
            int startBar, loopBar, endBar;
            using (new EditorGUILayout.HorizontalScope())
            {
                startBar = EditorGUILayout.IntField("Start Bar", TimeToBar(_start));
                loopBar = EditorGUILayout.IntField("Loop Bar", TimeToBar(_loopStart));
                endBar = EditorGUILayout.IntField("End Bar", TimeToBar(_end));
            }

            if (EditorGUI.EndChangeCheck())
            {
                _start = Mathf.Clamp(BarToTime(startBar), 0f, duration);
                _end = Mathf.Clamp(BarToTime(endBar), _start + 0.01f, duration);
                _loopStart = Mathf.Clamp(BarToTime(loopBar), _start, _end - 0.01f);
                CommitLoopPoints();
            }
        }

        private float SnapToGrid(float t) => AudioBpmEstimator.SnapToGrid(t, _bpm, _beatOffset, _beatsPerStep);

        private void DetectBpm()
        {
            if (AudioBpmEstimator.Estimate(_clipData, out AudioBpmEstimator.Result result))
            {
                _bpm = Mathf.Round(result.Bpm * 10f) / 10f;
                _beatOffset = result.BeatOffset;
            }
            else
            {
                LogHelper.Warn(LogTag, "Could not estimate a BPM for this clip - it may be too short, too quiet, or not rhythmic. Enter one by hand.");
            }
        }

        // ------------------------------------------------------------------ transport / apply

        private void DrawTransport()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
                if (GUILayout.Button("▶  Preview Loop", GUILayout.Height(32)))
                {
                    ApplyToVariant();
                    _previewHandle = SoundDataEditorPreview.PlayClip(_data, _clip);
                }

                GUI.backgroundColor = Color.white;
                if (GUILayout.Button("■ Stop", GUILayout.Width(70), GUILayout.Height(32)))
                {
                    SoundDataEditorPreview.Stop(_data);
                    _previewHandle = SoundHandle.None;
                }
            }

            using (new EditorGUI.DisabledScope(!_previewHandle.IsPlaying))
            using (new EditorGUILayout.HorizontalScope())
            {
                string playhead = "--:--";
                if (_previewHandle.IsPlaying)
                {
                    float t = Mathf.Max(0f, _previewHandle.Time);
                    playhead = _bpm > 0.01f ? $"{FormatTime(t)}  bar {TimeToBar(t)}" : FormatTime(t);
                }

                EditorGUILayout.LabelField(playhead, EditorStyles.miniLabel, GUILayout.Width(110f));

                if (GUILayout.Button("Mark Loop Start"))
                    MarkLoopStart();

                if (GUILayout.Button("Mark Loop End"))
                    MarkLoopEnd();
            }

            EditorGUILayout.HelpBox("Preview Loop writes these markers onto this clip's own Override Trim (turning it on if it wasn't) and plays it through the real AudioManager, in Edit Mode - no Play Mode needed. Every change you make while it's playing - dragging a handle, editing a field, Mark Loop Start/End, Snap - updates the loop LIVE, no need to Stop and Preview again.", MessageType.None);
        }

        // Grabs the live playhead and snaps it onto the beat grid (if one's been detected) rather
        // than trusting raw tap timing - a human pressing a button in reaction to what they hear is
        // reliably a little late, and the grid is usually a better answer than the exact millisecond
        // the mouse click landed on.
        private void MarkLoopStart()
        {
            float t = _previewHandle.Time;
            if (t < 0f)
                return;

            if (_bpm > 0.01f)
                t = SnapToGrid(t);

            _loopStart = Mathf.Clamp(t, _start, Mathf.Max(_start, _end - 0.01f));
            CommitLoopPoints();
        }

        private void MarkLoopEnd()
        {
            float t = _previewHandle.Time;
            if (t < 0f)
                return;

            if (_bpm > 0.01f)
                t = SnapToGrid(t);

            _end = Mathf.Clamp(t, Mathf.Max(_start, _loopStart) + 0.01f, _clipData.Duration);
            CommitLoopPoints();
        }

        private void ApplyToVariant()
        {
            SoundClip variant = _data.variants[_variantIndex];
            if (variant == null)
                return;

            Undo.RecordObject(_data, "Edit Loop Points");
            variant.overrideTrim = true;
            variant.startAt = _start;
            variant.endAt = _end;
            variant.loopStart = _loopStart;

            // `loop` lives on the SHARED SoundData, not per-clip - without this, everything above is
            // authored but AudioManager never engages ManualLoop, so playback just plays the trimmed
            // region once and fades out at the end instead of looping. This window's entire purpose
            // is authoring a loop, so turning it on here is the point, not a side effect.
            _data.loop = true;

            EditorUtility.SetDirty(_data);
        }

        // ------------------------------------------------------------------ crop

        private void DrawCropSection()
        {
            EditorGUILayout.LabelField("Save Size", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Writes a NEW file containing only [Start At, End At] - the head and tail outside that window will never play once this sound loops, so they're pure wasted build size. The source file is left untouched (delete it by hand once you've checked the crop, if nothing else references it). The new clip replaces this one on the SoundClip, with Start At/Loop Start/End At re-zeroed to match.",
                MessageType.None);

            bool canCrop = _end - _start > 0.05f;
            using (new EditorGUI.DisabledScope(!canCrop))
            {
                if (GUILayout.Button("Crop & Save New File", GUILayout.Height(24)))
                    CropAndSave();
            }
        }

        private void CropAndSave()
        {
            AudioClip cropped = AudioLoopCropper.Crop(_clipData, _start, _end);
            if (cropped == null)
            {
                EditorUtility.DisplayDialog("Crop failed", "Could not write the cropped file - see the Console.", "OK");
                return;
            }

            SoundClip variant = _data.variants[_variantIndex];
            Undo.RecordObject(_data, "Crop Loop Audio");
            variant.clip = cropped;
            variant.overrideTrim = true;
            variant.startAt = 0f;
            variant.loopStart = Mathf.Max(0f, _loopStart - _start);
            variant.endAt = 0f; // 0 = play to the end, and the end IS the crop boundary now.
            EditorUtility.SetDirty(_data);
            AssetDatabase.SaveAssets();

            LoadVariant();
        }
    }
}
