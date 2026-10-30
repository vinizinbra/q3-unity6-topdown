namespace RiftRaiders.Editor
{
    using System.Globalization;
    using System.IO;
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// Records the gameplay camera during Play mode to numbered PNGs (for evaluation GIFs of attacks / effects): every
    /// Editor update at most `fps` times per real second, Camera.main rendered into its own RenderTexture (no UI
    /// overlay), file name = frame index + milliseconds since Start so a slow Editor can be resampled to real speed.
    /// Driven from `unity cmd eval`: Start(folder, seconds), then poll IsRecording. SetFocus(point) frames a fixed world
    /// point instead of following the hero (same camera angle/height, just slid), so knockback can't push the subject out.
    /// </summary>
    public static class PlayModeFrameRecorder
    {
        static string _folder;
        static double _start, _end, _next, _interval;
        static int _index;
        static RenderTexture _target;
        static Vector3? _focus;

        static ScreenGrabber _screenGrabber;

        public static bool IsRecording => _target != null || _screenGrabber != null;
        public static int FramesWritten => _screenGrabber != null ? _screenGrabber.Index : _index;

        public static void Start(string folder, float seconds, int fps = 30, int width = 800, int height = 600)
        {
            Stop();
            if (Directory.Exists(folder) == true)
                Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);

            _folder = folder;
            _index = 0;
            _interval = 1.0 / fps;
            _start = _next = EditorApplication.timeSinceStartup;
            _end = _start + seconds;
            _target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            _target.Create();
            EditorApplication.update += Tick;
        }

        /// <summary>
        /// Like Start, but grabs the COMPOSITED Game view (ScreenCapture at end of frame) so Screen Space - Overlay UI
        /// (the HUD) is in the frames too. Full Game-view resolution; SetFocus does not apply. Poll IsRecording.
        /// </summary>
        public static void StartScreen(string folder, float seconds, int fps = 20)
        {
            Stop();
            if (Directory.Exists(folder) == true)
                Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);

            var go = new GameObject("PlayModeScreenGrabber") { hideFlags = HideFlags.HideAndDontSave };
            _screenGrabber = go.AddComponent<ScreenGrabber>();
            _screenGrabber.Begin(folder, seconds, fps);
        }

        public static void SetFocus(Vector3 worldPoint) => _focus = worldPoint;

        public static void ClearFocus() => _focus = null;

        public static void Stop()
        {
            if (_screenGrabber != null)
            {
                // Plain Destroy in Play mode: Stop() is also called from inside the grabber's own coroutine.
                if (EditorApplication.isPlaying == true)
                    Object.Destroy(_screenGrabber.gameObject);
                else
                    Object.DestroyImmediate(_screenGrabber.gameObject);
                _screenGrabber = null;
            }

            EditorApplication.update -= Tick;
            if (_target != null)
            {
                _target.Release();
                Object.DestroyImmediate(_target);
                _target = null;
            }
        }

        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (EditorApplication.isPlaying == false || now >= _end)
            {
                Stop();
                return;
            }

            if (now < _next)
                return;
            _next += _interval;
            if (_next < now)
                _next = now + _interval;

            Camera camera = Camera.main;
            if (camera == null)
                return;

            RenderTexture previousTarget = camera.targetTexture;
            Vector3 previousPosition = camera.transform.position;
            if (_focus.HasValue == true && camera.transform.forward.y < -0.01f)
            {
                // Slide the camera so the ground point it looks at (at the focus height) becomes the focus.
                float along = (previousPosition.y - _focus.Value.y) / -camera.transform.forward.y;
                Vector3 lookPoint = previousPosition + camera.transform.forward * along;
                camera.transform.position = previousPosition + (_focus.Value - lookPoint);
            }
            camera.targetTexture = _target;
            camera.Render();
            camera.targetTexture = previousTarget;
            camera.transform.position = previousPosition;

            RenderTexture previousActive = RenderTexture.active;
            RenderTexture.active = _target;
            var frame = new Texture2D(_target.width, _target.height, TextureFormat.RGB24, false);
            frame.ReadPixels(new Rect(0, 0, _target.width, _target.height), 0, 0);
            frame.Apply();
            RenderTexture.active = previousActive;

            int milliseconds = Mathf.RoundToInt((float)((now - _start) * 1000.0));
            File.WriteAllBytes(Path.Combine(_folder, string.Format(CultureInfo.InvariantCulture, "{0:000}_{1:00000}.png", _index++, milliseconds)), frame.EncodeToPNG());
            Object.DestroyImmediate(frame);
        }
    }

    // StartScreen's helper: ScreenCapture only sees the finished backbuffer at end of frame, which needs a coroutine.
    sealed class ScreenGrabber : MonoBehaviour
    {
        public int Index;

        public void Begin(string folder, float seconds, int fps) => StartCoroutine(Run(folder, seconds, fps));

        System.Collections.IEnumerator Run(string folder, float seconds, int fps)
        {
            var endOfFrame = new WaitForEndOfFrame();
            double start = EditorApplication.timeSinceStartup;
            double next = start;
            double interval = 1.0 / fps;

            while (EditorApplication.isPlaying == true && EditorApplication.timeSinceStartup - start < seconds)
            {
                yield return endOfFrame;

                double now = EditorApplication.timeSinceStartup;
                if (now < next)
                    continue;
                next = System.Math.Max(next + interval, now);

                Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
                int milliseconds = Mathf.RoundToInt((float)((now - start) * 1000.0));
                File.WriteAllBytes(Path.Combine(folder, string.Format(CultureInfo.InvariantCulture, "{0:000}_{1:00000}.png", Index++, milliseconds)), frame.EncodeToPNG());
                Object.Destroy(frame);
            }

            PlayModeFrameRecorder.Stop();
        }
    }
}
