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

        public static bool IsRecording => _target != null;
        public static int FramesWritten => _index;

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

        public static void SetFocus(Vector3 worldPoint) => _focus = worldPoint;

        public static void ClearFocus() => _focus = null;

        public static void Stop()
        {
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
}
